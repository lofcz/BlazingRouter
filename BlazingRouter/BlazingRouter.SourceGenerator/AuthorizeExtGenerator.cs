using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace BlazingRouter.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public class AuthorizeExtGenerator : IIncrementalGenerator
{
    internal readonly record struct SourcegenEnumMetadata
    {
        public string Name { get; }
        public string Namespace { get; }
        public string? PrefabEnumName { get; }
        public EquatableArray<PrefabMemberMetadata> PrefabMembers { get; }

        public SourcegenEnumMetadata(
            string name, 
            string nameSpace, 
            string? prefabEnumName = null,
            EquatableArray<PrefabMemberMetadata> prefabMembers = default)
        {
            Name = name;
            Namespace = nameSpace;
            PrefabEnumName = prefabEnumName;
            PrefabMembers = prefabMembers;
        }
    }
    
    internal readonly record struct RoleInfo
    {
        public int Value { get; }
        public string Name { get; }

        public RoleInfo(int value, string name)
        {
            Value = value;
            Name = name;
        }
    }

    internal readonly record struct PrefabMemberMetadata
    {
        public string MemberName { get; }
        public EquatableArray<RoleInfo> Roles { get; }
        public EquatableArray<RoleInfo> IncludedPrefabs { get; }

        public PrefabMemberMetadata(
            string memberName,
            EquatableArray<RoleInfo> roles,
            EquatableArray<RoleInfo> includedPrefabs)
        {
            MemberName = memberName;
            Roles = roles;
            IncludedPrefabs = includedPrefabs;
        }
    }
    
    static class EnumCache
    {
        // Statická cache pro mapování typu enumu na jeho hodnoty a názvy
        private static readonly Dictionary<ITypeSymbol, Dictionary<int, string>> Cache = [];

        public static string? GetEnumName(ITypeSymbol enumType, int value)
        {
            // Pokud cache obsahuje typ enumu, pokusíme se najít odpovídající hodnotu
            if (Cache.TryGetValue(enumType, out Dictionary<int, string>? valueToNameMap) && valueToNameMap.TryGetValue(value, out string? name))
            {
                return name;
            }

            return null;
        }

        public static void PopulateCache(ITypeSymbol enumType)
        {
            // Pokud cache již obsahuje typ enumu, nic neděláme
            if (Cache.ContainsKey(enumType))
            {
                return;
            }

            // Vytvoříme mapu hodnot a názvů pro daný enum
            Dictionary<int, string> valueToNameMap = new Dictionary<int, string>();

            if (enumType is INamedTypeSymbol { TypeKind: TypeKind.Enum } namedTypeSymbol)
            {
                foreach (IFieldSymbol? member in namedTypeSymbol.GetMembers().OfType<IFieldSymbol>())
                {
                    if (member.HasConstantValue && member.ConstantValue is int intValue)
                    {
                        valueToNameMap[intValue] = member.Name;
                    }
                }
            }

            // Přidáme mapu do cache
            Cache[enumType] = valueToNameMap;
        }
    }
    
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Fáze 1: Generování atributu
        context.RegisterPostInitializationOutput((piContext) =>
        {
            piContext.AddSource("RolePrefabAttribute.g.cs", @"
            using System;

            [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
            public class RolePrefabAttribute : Attribute
            {
                public object[] Roles { get; }
                public object[]? IncludedPrefabs { get; }

                public RolePrefabAttribute(params object[] roles)
                {
                    Roles = roles;
                }

                public RolePrefabAttribute(object[] roles, object[] includedPrefabs)
                {
                    Roles = roles;
                    IncludedPrefabs = includedPrefabs;
                }
            }
        ");
        });

        // Fáze 2: Registrace hlavní generace
        RegisterMainGeneration(context);
    }

    private static void RegisterMainGeneration(IncrementalGeneratorInitializationContext context)
    {
        // Provider pro AuthRoleEnum
        IncrementalValuesProvider<SourcegenEnumMetadata> enumDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "BlazingRouter.Shared.AuthRoleEnumAttribute",
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
        
        static RoleInfo[] ProcessAttributeArgument(AttributeArgumentSyntax argument, SemanticModel semanticModel)
        {
            if (argument.Expression is ArrayCreationExpressionSyntax arrayExpression)
            {
                // Pokud je argument pole, zpracujeme jednotlivé hodnoty
                return arrayExpression.Initializer?.Expressions
                    .Select(expr => GetExpressionValue(expr, semanticModel))
                    .Where(v => v != null)
                    .Select(v => v!.Value)
                    .ToArray() ?? [];
            }

            if (argument.Expression is ImplicitArrayCreationExpressionSyntax implicitArrayExpression)
            {
                // Pokud je argument implicitní pole, zpracujeme jednotlivé hodnoty
                return implicitArrayExpression.Initializer.Expressions
                    .Select(expr => GetExpressionValue(expr, semanticModel))
                    .Where(v => v != null)
                    .Select(v => v!.Value)
                    .ToArray();
            }

            if (argument.Expression is CollectionExpressionSyntax collectionExpressionSyntax)
            {
                return ProcessCollectionExpression(collectionExpressionSyntax, semanticModel);
            }

            // Pokud je argument jednoduchý výraz, zpracujeme ho přímo
            RoleInfo? value = GetExpressionValue(argument.Expression, semanticModel);
            return value != null ? [value.Value] : [];
        }
        
        static RoleInfo[] ProcessCollectionExpression(CollectionExpressionSyntax collectionExpression, SemanticModel semanticModel)
        {
            List<RoleInfo> results = [];

            foreach (CollectionElementSyntax element in collectionExpression.Elements)
            {
                if (element is ExpressionElementSyntax expressionElement)
                {
                    // Zpracujeme jednotlivý výraz
                    RoleInfo? value = GetExpressionValue(expressionElement.Expression, semanticModel);
                    if (value != null)
                    {
                        results.Add(value.Value);
                    }
                }
                else if (element is SpreadElementSyntax spreadElement)
                {
                    // Rozsahy (např. ...OtherCollection) nejsou podporovány v tomto kontextu
                    // Můžete přidat vlastní logiku, pokud je potřeba
                    throw new NotSupportedException("Spread elements are not supported in this context.");
                }
            }

            return results.ToArray();
        }


        static RoleInfo? GetExpressionValue(ExpressionSyntax expression, SemanticModel semanticModel)
        {
            Optional<object?> constantValue = semanticModel.GetConstantValue(expression);

            ISymbol? symbolInfo2 = ModelExtensions.GetSymbolInfo(semanticModel, expression).Symbol;

            // quick path for syntax [Enum(Role.X)]
            if (symbolInfo2 is IFieldSymbol fieldSymbol2 && fieldSymbol2.ContainingType.TypeKind == TypeKind.Enum)
            {
                string? name = fieldSymbol2.Name;

                if (constantValue.HasValue)
                {
                    return new RoleInfo((int)constantValue.Value, name);
                }
            }
            
            // if we got something else, for example binary expr [Rnum(Role.X + 1)]
            if (constantValue is { HasValue: true, Value: int intValue })
            {
                // Získáme typ výrazu
                ITypeSymbol? typeSymbol = semanticModel.GetTypeInfo(expression).Type;

                // Pokud je typ enum, pokusíme se najít odpovídající hodnotu v cache
                if (typeSymbol is INamedTypeSymbol { TypeKind: TypeKind.Enum } namedTypeSymbol)
                {
                    // Naplníme cache, pokud ještě není naplněna
                    EnumCache.PopulateCache(namedTypeSymbol);

                    // Pokusíme se najít odpovídající název v cache
                    string? name = EnumCache.GetEnumName(namedTypeSymbol, intValue);

                    if (name != null)
                    {
                        return new RoleInfo(intValue, name);
                    }
                }
            }
            
            return null;
        }

        static HashSet<RoleInfo> GetAllRolesForPrefab(
                    IFieldSymbol prefabField,
                    SemanticModel semanticModel,
                    HashSet<string> processedPrefabs)
                {
                    HashSet<RoleInfo> roles = [];
                    
                    if (!processedPrefabs.Add(prefabField.Name))
                        return roles;

                    AttributeData? attr = prefabField.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.Name is "RolePrefabAttribute");
                    
                    if (attr?.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax attributeSyntax 
                        || attributeSyntax.ArgumentList is null)
                        return roles;

                    foreach (AttributeArgumentSyntax arg in attributeSyntax.ArgumentList.Arguments)
                    {
                        RoleInfo[] includedPrefabs = ProcessAttributeArgument(arg, semanticModel);
                        
                        foreach (RoleInfo includedPrefab in includedPrefabs)
                        {
                            IFieldSymbol? includedField = prefabField.ContainingType.GetMembers()
                                .OfType<IFieldSymbol>()
                                .FirstOrDefault(f => f.Name == includedPrefab.Name);

                            if (includedField != null)
                            {
                                HashSet<RoleInfo> nestedRoles = GetAllRolesForPrefab(includedField, semanticModel, processedPrefabs);
                                foreach (RoleInfo role in nestedRoles)
                                {
                                    roles.Add(role);
                                }
                            }
                            else
                            {
                                roles.Add(includedPrefab);
                            }
                        }
                    }

                    return roles;
                }
        
        IncrementalValuesProvider<SourcegenEnumMetadata> prefabEnumDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "BlazingRouter.Shared.AuthRolePrefabsEnumAttribute",
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
                transform: static (ctx, _) =>
                {
                    if (ctx.TargetSymbol is not INamedTypeSymbol prefabEnum) return default;
                   
                    PrefabMemberMetadata[] prefabMembers = prefabEnum.GetMembers()
                        .OfType<IFieldSymbol>()
                        .Select(field =>
                        {
                            HashSet<RoleInfo> allRoles = GetAllRolesForPrefab(field, ctx.SemanticModel, []);
                        
                            RoleInfo[] includedPrefabs = field.GetAttributes()
                                .FirstOrDefault(a => a.AttributeClass?.Name is "RolePrefabAttribute")
                                ?.ApplicationSyntaxReference
                                ?.GetSyntax() is AttributeSyntax { ArgumentList.Arguments.Count: > 1 } attr
                                ? ProcessAttributeArgument(attr.ArgumentList.Arguments[1], ctx.SemanticModel)
                                : [];

                            return new PrefabMemberMetadata(
                                field.Name,
                                new EquatableArray<RoleInfo>(allRoles.ToArray()),
                                new EquatableArray<RoleInfo>(includedPrefabs));
                        })
                        .ToArray();

                    return new SourcegenEnumMetadata(
                        prefabEnum.Name,
                        prefabEnum.ContainingNamespace.ToString(),
                        prefabEnumName: prefabEnum.Name,
                        prefabMembers: new EquatableArray<PrefabMemberMetadata>(prefabMembers));
                });
        
        IncrementalValuesProvider<(SourcegenEnumMetadata Left, ImmutableArray<SourcegenEnumMetadata> Right)> combined = enumDeclarations.Combine(prefabEnumDeclarations.Collect());

        // always generate [AuthRole]
        context.RegisterSourceOutput(combined,
            static (spc, pair) => Execute(
                pair.Left,  // roleEnum metadata
                pair.Right.FirstOrDefault(),
                spc));

        // [RolePrefab] gen
        context.RegisterSourceOutput(combined,
            static (spc, pair) => ExecutePrefab(
                pair.Left,  // roleEnum metadata
                pair.Right.FirstOrDefault(),
                spc));
    }

    private static bool IsSyntaxTargetForGeneration(SyntaxNode node) => node is EnumDeclarationSyntax { AttributeLists.Count: > 0 };

    private static INamedTypeSymbol? GetSemanticTargetForGeneration(GeneratorAttributeSyntaxContext context)
    {
        EnumDeclarationSyntax enumDeclaration = (EnumDeclarationSyntax)context.TargetNode;
        SemanticModel model = context.SemanticModel;

        if (ModelExtensions.GetDeclaredSymbol(model, enumDeclaration) is not INamedTypeSymbol enumSymbol)
        {
            return null;
        }

        // check for [AuthRoleEnum]
        return HasAuthRoleEnumAttribute(enumSymbol) ? enumSymbol : null;
    }
    
    private static bool HasAuthRoleEnumAttribute(INamedTypeSymbol enumSymbol)
        => enumSymbol.GetAttributes().Any(attr => attr.AttributeClass?.Name is "AuthRoleEnumAttribute");

    private static void Execute(
    SourcegenEnumMetadata roleEnum,
    SourcegenEnumMetadata? prefabEnum,
    SourceProductionContext context)
    {
        string sharedPrefabLists = prefabEnum is not null ?
            $$"""
                
                public static class {{prefabEnum.Value.Name}}Cls
                {
                    {{string.Join("\n", prefabEnum.Value.PrefabMembers.Select(p => $$"""
                    public static readonly List<{{roleEnum.Name}}> {{p.MemberName}}AuthRoles = [ {{string.Join(", ", p.Roles.Where(x => !string.IsNullOrEmpty(x.Name)).Select(r => $"{roleEnum.Name}.{r.Name}"))}} ];
                    """))}}
                
                    {{string.Join("\n", prefabEnum.Value.PrefabMembers.Select(p => $$"""
                    public static readonly List<{{roleEnum.Name}}AuthRole> {{p.MemberName}}Roles = [ {{string.Join(", ", p.Roles.Where(x => !string.IsNullOrEmpty(x.Name)).Select(r => $"new {roleEnum.Name}AuthRole({roleEnum.Name}.{r.Name})"))}} ];
                    """))}}
                }
                """ : string.Empty;
        
        string deconstructMethod = prefabEnum is not null
            ? $$"""
                    public static class {{prefabEnum.Value.Name}}Extensions
                    {
                        /// <summary>
                        /// Deconstructs a role prefab into its constituent roles.
                        /// </summary>
                        /// <param name="prefab">The role prefab to deconstruct.</param>
                        /// <returns>A list of roles that the prefab represents.</returns>
                        public static List<{{roleEnum.Name}}> DeconstructPrefab(this {{prefabEnum.Value.Name}} prefab)
                        {
                            return prefab switch
                            {
                                {{string.Join("\n", prefabEnum.Value.PrefabMembers.Select(p => $$"""
                                                                                                     {{prefabEnum.Value.Name}}.{{p.MemberName}} => {{prefabEnum.Value.Name}}Cls.{{p.MemberName}}AuthRoles,
                                                                                                 """))}}
                                _ => new List<{{roleEnum.Name}}>()
                            };
                        }
                    }
                """
            : string.Empty;
        
       string prefabCtors = prefabEnum is not null
        ? $$"""

                    /// <summary>
                    /// Initializes a new instance of the authorization attribute with roles defined by a role prefab.
                    /// </summary>
                    /// <param name="prefab">A predefined role prefab that represents a collection of roles.
                    /// The user must have at least one of the roles defined in the prefab to access the resource.</param>
                    public AuthorizeExt({{prefabEnum.Value.Name}} prefab)
                    {
                        roles = prefab switch
                        {
                            {{string.Join("\n", prefabEnum.Value.PrefabMembers.Select(p => $$"""
                                            {{prefabEnum.Value.Name}}.{{p.MemberName}} => {{prefabEnum.Value.Name}}Cls.{{p.MemberName}}Roles,
                            """))}}
                            _ => new List<{{roleEnum.Name}}AuthRole>()
                        };
                    }

                    /// <summary>
                    /// Initializes a new instance of the authorization attribute with roles defined by multiple role prefabs.
                    /// </summary>
                    /// <param name="prefabs">An array of predefined role prefabs. Each prefab represents a collection of roles.
                    /// The resulting role set is a union of all roles from all prefabs.
                    /// The user must have at least one role from the combined set to access the resource.</param>
                    public AuthorizeExt(params {{prefabEnum.Value.Name}}[] prefabs)
                    {
                        var allRoles = new HashSet<{{roleEnum.Name}}AuthRole>();
                        foreach (var prefab in prefabs)
                        {
                            switch (prefab)
                            {
                                {{string.Join("\n", prefabEnum.Value.PrefabMembers.Select(p => $$"""
                                                                                                 case {{prefabEnum.Value.Name}}.{{p.MemberName}}:
                                                                                                     allRoles.UnionWith({{prefabEnum.Value.Name}}Cls.{{p.MemberName}}Roles);
                                                                                                     break;
                                                                                                 """))}}
                            }
                        }
                        
                        roles = allRoles.ToList();
                    }

            """
        : string.Empty;
            
        string source = $$"""
                          using System;
                          using System.Collections.Generic;
                          using System.Linq;
                          using BlazingRouter.Shared;
                          using {{roleEnum.Namespace}};

                          #nullable enable

                          namespace {{roleEnum.Namespace}}
                          {{{sharedPrefabLists}}
                              public sealed class {{roleEnum.Name}}AuthRole : IRole
                              {
                                  public {{roleEnum.Name}} Role { get; }
                                  public string Name => Role.ToString();
                                  public int Value => (int)Role;
                          
                                  public {{roleEnum.Name}}AuthRole({{roleEnum.Name}} role)
                                  {
                                      Role = role;
                                  }
                          
                                  public static implicit operator {{roleEnum.Name}}AuthRole({{roleEnum.Name}} role)
                                      => new(role);
                          
                                  public override bool Equals(object? obj)
                                      => obj is {{roleEnum.Name}}AuthRole other && Role.Equals(other.Role);
                          
                                  public override int GetHashCode()
                                      => Role.GetHashCode();
                          
                                  public override string ToString()
                                      => Role.ToString();
                              }
                          
                              public static class {{roleEnum.Name}}Extensions
                              {
                                    public static IReadOnlyList<IRole> ToAuthRoles(this IEnumerable<{{roleEnum.Name}}> roles)
                                        => roles.Select(r => new {{roleEnum.Name}}AuthRole(r)).ToList();
                           
                                    public static IReadOnlyList<{{roleEnum.Name}}> FromAuthRoles(this IEnumerable<IRole> roles)
                                        => roles.OfType<{{roleEnum.Name}}AuthRole>().Select(r => r.Role).ToList();
                           
                                    public static {{roleEnum.Name}}? TryParseRole(this IRole role)
                                        => role is {{roleEnum.Name}}AuthRole typedRole ? typedRole.Role : null;
                              }
                              
                              /// <summary>
                              /// Specifies that access to a class (controller) or method (action) requires authorization.
                              /// </summary>
                              [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
                              public sealed class AuthorizeExt : AuthorizeExtAttributeBase
                              {
                                  private readonly List<{{roleEnum.Name}}AuthRole>? roles;
                                  public override IReadOnlyList<IRole> Roles => roles;
                          
                                  /// <summary>
                                  /// User must be authenticated to access the resource. No roles are required.
                                  /// </summary>
                                  public AuthorizeExt()
                                  {
                                  
                                  }
                          
                                  /// <summary>
                                  /// User must have the role specified to access the resource.
                                  /// </summary>
                                  /// <param name="role">The role required to access the resource.</param>
                                  public AuthorizeExt({{roleEnum.Name}} role)
                                  {
                                      this.roles = new List<{{roleEnum.Name}}AuthRole> { new(role) };
                                  }
                          
                                  /// <summary>
                                  /// Initializes a new instance of the authorization attribute with multiple required roles.
                                  /// </summary>
                                  /// <param name="roles">An array of roles. User must have at least one of these roles to access the resource.</param>
                                  public AuthorizeExt(params {{roleEnum.Name}}[] roles)
                                  {
                                      this.roles = roles.Select(r => new {{roleEnum.Name}}AuthRole(r)).ToList();
                                  }
                                  
                                  /// <summary>
                                  /// Initializes a new instance of the authorization attribute with a collection of required roles.
                                  /// </summary>
                                  /// <param name="roles">A collection of roles. User must have at least one of these roles to access the resource.</param>
                                  public AuthorizeExt(IEnumerable<{{roleEnum.Name}}> roles)
                                  {
                                      this.roles = roles.Select(r => new {{roleEnum.Name}}AuthRole(r)).ToList();
                                  }
                                  
                                  /// <summary>
                                  /// Initializes a new instance of the authorization attribute with a list of required roles.
                                  /// </summary>
                                  /// <param name="roles">A list of roles. User must have at least one of these roles to access the resource.</param>
                                  public AuthorizeExt(List<{{roleEnum.Name}}> roles)
                                  {
                                      this.roles = roles.Select(r => new {{roleEnum.Name}}AuthRole(r)).ToList();
                                  }{{prefabCtors}}
                              }{{deconstructMethod}}
                          }
                          """;

        context.AddSource($"{roleEnum.Name}AuthorizeExt.g.cs", source);
        ExecuteExtension(roleEnum, context);
    }

    private static void ExecuteExtension(SourcegenEnumMetadata roleEnum, SourceProductionContext context)
    {
        string source = $$"""
                          using System;
                          using System.Reflection;
                          using Microsoft.Extensions.DependencyInjection;
                          using BlazingRouter.Shared;
                          using BlazingRouter;
                          using Route = BlazingRouter.Route;

                          #nullable enable

                          namespace {{roleEnum.Namespace}}
                          {
                              public static class {{roleEnum.Name}}BlazingRouterExtensions
                              {
                                  public static IBlazingRouterBuilder<{{roleEnum.Name}}> AddBlazingRouter(this IServiceCollection services, Assembly? assembly = null)
                                  {
                                      services.AddSingleton<RouteManager>();
                                      BlazingRouterBuilder<{{roleEnum.Name}}> builder = new BlazingRouterBuilder<{{roleEnum.Name}}>();
                                      return builder;
                                  }
                              }
                              
                              public static class BlazingRouterGenerated
                              {
                                  /// <summary>
                                  /// Creates a route from a route, e.g. /test/ping. Segments should be delimited by "/"
                                  /// </summary>
                                  /// <param name="pattern">See <see cref="Route"/> for syntax</param>
                                  /// <param name="handler">Type (of a page) associated with this route</param>
                                  /// <param name="authorizedRoles">User must be at least in one of the roles listed to access the route</param>
                                  /// <param name="priority">Optional priority, use numbers > 0 for higher priority</param>
                                  public static Route CreateRoute(string pattern, Type handler, List<{{roleEnum.Name}}> authorizedRoles, int priority = 0)
                                  {
                                       List<IRole> roles = [];
                                       
                                       foreach ({{roleEnum.Name}} role in authorizedRoles)
                                       {
                                            roles.Add(new {{roleEnum.Name}}AuthRole(role));
                                       }
                                  
                                       return new Route(pattern, handler, roles, priority);
                                  }
                              }
                          }
                          """;

        context.AddSource($"{roleEnum.Name}BlazingRouterExtensions.g.cs", source);
    }

    private static void ExecutePrefab(SourcegenEnumMetadata roleEnumMetadata, SourcegenEnumMetadata prefabEnumMetadata, SourceProductionContext context)
    {
        StringBuilder proxyBuilder = new StringBuilder();

    proxyBuilder.AppendLine($"namespace {prefabEnumMetadata.Namespace}");
    proxyBuilder.AppendLine("{");
    proxyBuilder.AppendLine($"    /// <summary>");
    proxyBuilder.AppendLine($"    /// Generated documentation interface for {prefabEnumMetadata.Name}.");
    proxyBuilder.AppendLine($"    /// </summary>");
    proxyBuilder.AppendLine($"    public interface {prefabEnumMetadata.Name}Docs");
    proxyBuilder.AppendLine("    {");

    foreach (PrefabMemberMetadata prefab in prefabEnumMetadata.PrefabMembers)
    {
        string resolvedRoles = prefab.Roles.Count is 0 ? "none" : string.Join(", ", prefab.Roles.OrderBy(x => x.Name, StringComparer.InvariantCulture).Select(r => $"<see cref=\"{roleEnumMetadata.Name}.{r.Name}\"/>"));

        proxyBuilder.AppendLine($"        /// <summary>");
        proxyBuilder.AppendLine($"        /// Resolved roles: {resolvedRoles}");
        proxyBuilder.AppendLine($"        /// </summary>");
        proxyBuilder.AppendLine($"        public void {prefab.MemberName}();");
    }

    proxyBuilder.AppendLine("    }");
    proxyBuilder.AppendLine("}");

    context.AddSource($"{prefabEnumMetadata.Name}Docs.g.cs", proxyBuilder.ToString());
        
        string source = $$"""
            using System;
            using System.Reflection;
            using System.Collections.Generic;
            using System.Linq;
            using {{roleEnumMetadata.Namespace}};

            #nullable enable

            namespace {{roleEnumMetadata.Namespace}}
            {
                [AttributeUsage(AttributeTargets.Field)]
                public sealed class RolePrefabAttribute : Attribute
                {
                    public {{roleEnumMetadata.Name}}[] Roles { get; }
                    public {{prefabEnumMetadata.Name}}[]? IncludedPrefabs { get; }

                    public RolePrefabAttribute(params {{roleEnumMetadata.Name}}[] roles)
                    {
                        Roles = roles.ToArray();
                    }

                    public RolePrefabAttribute(
                        {{roleEnumMetadata.Name}}[] roles, 
                        {{prefabEnumMetadata.Name}}[] includedPrefabs)
                    {
                        Roles = roles;
                        IncludedPrefabs = includedPrefabs;
                    }
                    
                    public RolePrefabAttribute(
                        {{roleEnumMetadata.Name}}[] roles, 
                        {{prefabEnumMetadata.Name}} basedOn)
                    {
                        Roles = roles;
                        IncludedPrefabs = [ basedOn ];
                    }
                }
            }
            """;

        context.AddSource($"{roleEnumMetadata.Name}RolePrefab.g.cs", source);
    }
}