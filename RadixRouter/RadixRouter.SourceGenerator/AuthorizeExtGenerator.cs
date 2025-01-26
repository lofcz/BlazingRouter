using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Linq;

namespace RadixRouter.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public class AuthorizeExtGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Získáme všechny enum deklarace s [AuthRoleEnum] atributem
        IncrementalValuesProvider<INamedTypeSymbol?> enumDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .Where(static m => m is not null);

        // Registrujeme výstup
        context.RegisterSourceOutput(enumDeclarations,
            static (spc, enumSymbol) => Execute(enumSymbol!, spc));
    }

    private static bool IsSyntaxTargetForGeneration(SyntaxNode node)
        => node is EnumDeclarationSyntax { AttributeLists.Count: > 0 };

    private static INamedTypeSymbol? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        EnumDeclarationSyntax enumDeclaration = (EnumDeclarationSyntax)context.Node;
        SemanticModel model = context.SemanticModel;
        
        INamedTypeSymbol? enumSymbol = model.GetDeclaredSymbol(enumDeclaration) as INamedTypeSymbol;
        if (enumSymbol == null) return null;

        // Kontrola, zda má enum [AuthRoleEnum] atribut
        return HasAuthRoleEnumAttribute(enumSymbol) ? enumSymbol : null;
    }

    private static bool HasAuthRoleEnumAttribute(INamedTypeSymbol enumSymbol)
        => enumSymbol.GetAttributes().Any(attr => attr.AttributeClass?.Name == "AuthRoleEnumAttribute");

    private static void Execute(INamedTypeSymbol enumSymbol, SourceProductionContext context)
    {
        string source = $@"
using System;
using System.Collections.Generic;
using System.Linq;
using {enumSymbol.ContainingNamespace};

#nullable enable

namespace {enumSymbol.ContainingNamespace}
{{
    public sealed class {enumSymbol.Name}AuthRole : IRole
    {{
        public {enumSymbol.Name} Role {{ get; }}
        public string Name => Role.ToString();
        public int Value => (int)Role;

        public {enumSymbol.Name}AuthRole({enumSymbol.Name} role)
        {{
            Role = role;
        }}

        public static implicit operator {enumSymbol.Name}AuthRole({enumSymbol.Name} role)
            => new(role);

        public override bool Equals(object? obj)
            => obj is {enumSymbol.Name}AuthRole other && Role.Equals(other.Role);

        public override int GetHashCode()
            => Role.GetHashCode();

        public override string ToString()
            => Role.ToString();
    }}

    public static class {enumSymbol.Name}Extensions
    {{
        public static IEnumerable<IRole> ToAuthRoles(this IEnumerable<{enumSymbol.Name}> roles)
            => roles.Select(r => new {enumSymbol.Name}AuthRole(r));

        public static IEnumerable<{enumSymbol.Name}> FromAuthRoles(this IEnumerable<IRole> roles)
            => roles.OfType<{enumSymbol.Name}AuthRole>().Select(r => r.Role);

        public static {enumSymbol.Name}? TryParseRole(this IRole role)
            => role is {enumSymbol.Name}AuthRole typedRole ? typedRole.Role : null;
    }}

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class AuthorizeExt : AuthorizeExtAttributeBase
    {{
        private readonly List<{enumSymbol.Name}AuthRole> _roles;
        public override IEnumerable<IRole> Roles => _roles;

        public AuthorizeExt({enumSymbol.Name} role)
        {{
            _roles = new List<{enumSymbol.Name}AuthRole> {{ new(role) }};
        }}

        public AuthorizeExt(params {enumSymbol.Name}[] roles)
        {{
            _roles = roles.Select(r => new {enumSymbol.Name}AuthRole(r)).ToList();
        }}
    }}
}}";

        context.AddSource($"{enumSymbol.Name}AuthorizeExt.g.cs", source);
    }
}