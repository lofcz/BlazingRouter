using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazingRouter.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class EnumXmlDocumentationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ENUM001";

    private static readonly LocalizableString Title = "Missing or invalid XML documentation";
    private static readonly LocalizableString MessageFormat = "Enum member '{0}' should have XML documentation in format: /// <inheritdoc cref=\"{1}.{0}\"/>. Found: {2}";
    private static readonly LocalizableString Description = "All enum members should have proper XML documentation.";
    private const string Category = "Documentation";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.EnumDeclaration);
    }

    private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        EnumDeclarationSyntax enumDeclaration = (EnumDeclarationSyntax)context.Node;

        bool hasAttribute = enumDeclaration.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(a => a.Name.ToString() == "AuthRolePrefabsEnum");

        if (!hasAttribute)
            return;

        string enumName = enumDeclaration.Identifier.Text;
        string expectedDocsName = $"{enumName}Docs";

        foreach (EnumMemberDeclarationSyntax member in enumDeclaration.Members)
        {
            string memberName = member.Identifier.Text;
            string expectedDoc = $"<inheritdoc cref=\"{expectedDocsName}.{memberName}\"/>";
            
            SyntaxTriviaList? trivia = member.GetLeadingTrivia();
            
            string? xmlDoc = member.GetLeadingTrivia()
                .Where(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
                .Select(t => t.ToString().Trim())
                .FirstOrDefault();

            System.Diagnostics.Debug.WriteLine($"Member: {memberName}");
            System.Diagnostics.Debug.WriteLine($"Trivia count: {member.GetLeadingTrivia().Count}");
            foreach (SyntaxTrivia t in member.GetLeadingTrivia())
            {
                System.Diagnostics.Debug.WriteLine($"Trivia kind: {t.Kind()}, Value: '{t}'");
            }
            System.Diagnostics.Debug.WriteLine($"Found XML: '{xmlDoc}'");
            System.Diagnostics.Debug.WriteLine($"Expected: '{expectedDoc}'");

            if (xmlDoc == null || !xmlDoc.Equals(expectedDoc, StringComparison.Ordinal))
            {
                Diagnostic diagnostic = Diagnostic.Create(
                    Rule,
                    member.GetLocation(),
                    memberName,
                    expectedDocsName,
                    xmlDoc ?? trivia?.ToString() ?? "vubec nic");

                context.ReportDiagnostic(diagnostic);
            }
        }
    }

}