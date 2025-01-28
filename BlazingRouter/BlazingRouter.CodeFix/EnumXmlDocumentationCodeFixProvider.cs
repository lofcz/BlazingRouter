using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Composition;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Text;
using BlazingRouter.Analyzer;
using Document = Microsoft.CodeAnalysis.Document;

namespace BlazingRouter.CodeFix;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EnumXmlDocumentationCodeFixProvider)), Shared]
public class EnumXmlDocumentationCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds => [EnumXmlDocumentationAnalyzer.DiagnosticId];
    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        Diagnostic diagnostic = context.Diagnostics.First();
        TextSpan diagnosticSpan = diagnostic.Location.SourceSpan;

        EnumMemberDeclarationSyntax? enumMember = root.FindToken(diagnosticSpan.Start).Parent.AncestorsAndSelf().OfType<EnumMemberDeclarationSyntax>().First();
        EnumDeclarationSyntax? enumDeclaration = enumMember.Parent as EnumDeclarationSyntax;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add link to the \u2728 generated XML documentation",
                createChangedDocument: c => AddXmlDocumentationAsync(context.Document, enumMember, enumDeclaration, c),
                equivalenceKey: nameof(EnumXmlDocumentationCodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> AddXmlDocumentationAsync(Document document, EnumMemberDeclarationSyntax enumMember, EnumDeclarationSyntax enumDeclaration, CancellationToken cancellationToken)
    {
        string enumName = enumDeclaration.Identifier.Text;
        string memberName = enumMember.Identifier.Text;
        string expectedDocsName = $"{enumName}Docs";
        
        string indentation = enumMember.GetLeadingTrivia()
            .Where(t => t.IsKind(SyntaxKind.WhitespaceTrivia))
            .Select(t => t.ToString())
            .FirstOrDefault() ?? "    ";
        
        string xmlDoc = $"{indentation}/// <inheritdoc cref=\"{expectedDocsName}.{memberName}\"/>\r\n{indentation}";
        SyntaxTriviaList newTrivia = SyntaxFactory.ParseLeadingTrivia(xmlDoc);

        EnumMemberDeclarationSyntax newMember = enumMember
            .WithLeadingTrivia(newTrivia.Concat(enumMember.GetLeadingTrivia()
                .Where(t => !t.ToString().TrimStart().StartsWith("///") && !t.IsKind(SyntaxKind.WhitespaceTrivia))));

        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken);
        SyntaxNode? newRoot = root.ReplaceNode(enumMember, newMember);

        return document.WithSyntaxRoot(newRoot);
    }
}