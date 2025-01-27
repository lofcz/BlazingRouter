using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Linq;
using SourceGeneratorHelpers.External;

namespace RadixRouter.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public class AuthorizeExtGenerator : IIncrementalGenerator
{
    public readonly record struct SourcegenEnumMetadata
    {
        public string Name { get; }
        public string Namespace { get; }

        public SourcegenEnumMetadata(string name, string nameSpace)
        {
            Name = name;
            Namespace = nameSpace;
        }
    }
    
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Získáme všechny enum deklarace s [AuthRoleEnum] atributem
        IncrementalValuesProvider<SourcegenEnumMetadata> enumDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "RadixRouter.Shared.AuthRoleEnumAttribute",
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
                transform: static (ctx, _) =>
                {
                    INamedTypeSymbol? enumSymbol = GetSemanticTargetForGeneration(ctx);
                    if (enumSymbol is null) return default;

                    return new SourcegenEnumMetadata(
                        enumSymbol.Name,
                        enumSymbol.ContainingNamespace.ToString()
                    );
                });

        // Registrujeme výstup
        context.RegisterSourceOutput(enumDeclarations,
            static (spc, enumToGenerate) => Execute(enumToGenerate, spc));
    }
    
    private static bool IsSyntaxTargetForGeneration(SyntaxNode node) => node is EnumDeclarationSyntax { AttributeLists.Count: > 0 };

    private static INamedTypeSymbol? GetSemanticTargetForGeneration(GeneratorAttributeSyntaxContext context)
    {
        EnumDeclarationSyntax enumDeclaration = (EnumDeclarationSyntax)context.TargetNode;
        SemanticModel model = context.SemanticModel;

        if (model.GetDeclaredSymbol(enumDeclaration) is not INamedTypeSymbol enumSymbol)
        {
            return null;
        }

        // Kontrola, zda má enum [AuthRoleEnum] atribut
        return HasAuthRoleEnumAttribute(enumSymbol) ? enumSymbol : null;
    }

    private static bool HasAuthRoleEnumAttribute(INamedTypeSymbol enumSymbol)
        => enumSymbol.GetAttributes().Any(attr => attr.AttributeClass?.Name is "AuthRoleEnumAttribute");

   private static void Execute(SourcegenEnumMetadata sourcegenEnumSymbol, SourceProductionContext context)
    {
        string source = $$"""
                          using System;
                          using System.Collections.Generic;
                          using System.Linq;
                          using RadixRouter.Shared;
                          using {{sourcegenEnumSymbol.Namespace}};

                          #nullable enable

                          namespace {{sourcegenEnumSymbol.Namespace}}
                          {
                              public sealed class {{sourcegenEnumSymbol.Name}}AuthRole : IRole
                              {
                                  public {{sourcegenEnumSymbol.Name}} Role { get; }
                                  public string Name => Role.ToString();
                                  public int Value => (int)Role;
                          
                                  public {{sourcegenEnumSymbol.Name}}AuthRole({{sourcegenEnumSymbol.Name}} role)
                                  {
                                      Role = role;
                                  }
                          
                                  public static implicit operator {{sourcegenEnumSymbol.Name}}AuthRole({{sourcegenEnumSymbol.Name}} role)
                                      => new(role);
                          
                                  public override bool Equals(object? obj)
                                      => obj is {{sourcegenEnumSymbol.Name}}AuthRole other && Role.Equals(other.Role);
                          
                                  public override int GetHashCode()
                                      => Role.GetHashCode();
                          
                                  public override string ToString()
                                      => Role.ToString();
                              }
                          
                              public static class {{sourcegenEnumSymbol.Name}}Extensions
                              {
                                    public static IReadOnlyList<IRole> ToAuthRoles(this IEnumerable<{{sourcegenEnumSymbol.Name}}> roles)
                                        => roles.Select(r => new {{sourcegenEnumSymbol.Name}}AuthRole(r)).ToList();
                           
                                    public static IReadOnlyList<{{sourcegenEnumSymbol.Name}}> FromAuthRoles(this IEnumerable<IRole> roles)
                                        => roles.OfType<{{sourcegenEnumSymbol.Name}}AuthRole>().Select(r => r.Role).ToList();
                           
                                    public static {{sourcegenEnumSymbol.Name}}? TryParseRole(this IRole role)
                                        => role is {{sourcegenEnumSymbol.Name}}AuthRole typedRole ? typedRole.Role : null;
                              }
                          
                              [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
                              public sealed class AuthorizeExt : AuthorizeExtAttributeBase
                              {
                                  private readonly List<{{sourcegenEnumSymbol.Name}}AuthRole> _roles;
                                  public override IReadOnlyList<IRole> Roles => _roles;
                          
                                  public AuthorizeExt({{sourcegenEnumSymbol.Name}} role)
                                  {
                                      _roles = new List<{{sourcegenEnumSymbol.Name}}AuthRole> { new(role) };
                                  }
                          
                                  public AuthorizeExt(params {{sourcegenEnumSymbol.Name}}[] roles)
                                  {
                                      _roles = roles.Select(r => new {{sourcegenEnumSymbol.Name}}AuthRole(r)).ToList();
                                  }
                                  
                                  public AuthorizeExt(IEnumerable<{{sourcegenEnumSymbol.Name}}> roles)
                                  {
                                      _roles = roles.Select(r => new {{sourcegenEnumSymbol.Name}}AuthRole(r)).ToList();
                                  }
                                  
                                  public AuthorizeExt(List<{{sourcegenEnumSymbol.Name}}> roles)
                                  {
                                      _roles = roles.Select(r => new {{sourcegenEnumSymbol.Name}}AuthRole(r)).ToList();
                                  }
                              }
                          }
                          """;

        context.AddSource($"{sourcegenEnumSymbol.Name}AuthorizeExt.g.cs", source);
        ExecuteExtension(sourcegenEnumSymbol, context);
    }

    private static void ExecuteExtension(SourcegenEnumMetadata sourcegenEnumSymbol, SourceProductionContext context)
    {
        string source = $$"""
                          using System;
                          using System.Reflection;
                          using Microsoft.Extensions.DependencyInjection;

                          #nullable enable
                          
                          namespace {{sourcegenEnumSymbol.Namespace}}
                          {
                              public static class {{sourcegenEnumSymbol.Name}}BlazingRouterExtensions
                              {
                                  public static IBlazingRouterBuilder<{{sourcegenEnumSymbol.Name}}> AddBlazingRouter(this IServiceCollection services, Assembly? assembly = null)
                                  {
                                      services.AddSingleton<RouteManager>();
                                      BlazingRouterBuilder<{{sourcegenEnumSymbol.Name}}> builder = new BlazingRouterBuilder<{{sourcegenEnumSymbol.Name}}>();
                                      RouteManager.InitRouteManager(assembly ?? Assembly.GetExecutingAssembly(), builder);
                                      return builder;
                                  }
                              }
                          }
                          """;

        context.AddSource($"{sourcegenEnumSymbol.Name}BlazingRouterExtensions.g.cs", source);
    }
}