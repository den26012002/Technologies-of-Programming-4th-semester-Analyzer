using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Differencing;
using Microsoft.CodeAnalysis.Editing;

namespace AnalyzerTemplate
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AnalyzerTemplateCodeFixProvider)), Shared]
    public class AnalyzerTemplateCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(AnalyzerTemplateAnalyzer.MagicNumbersDiagnosticId, AnalyzerTemplateAnalyzer.UselessBackingFieldsDiagnosticId); }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            // See https://github.com/dotnet/roslyn/blob/main/docs/analyzers/FixAllProvider.md for more information on Fix All Providers
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            var magicNumbersDiagnostic = context.Diagnostics.FirstOrDefault(x => x.Id == "MagicNumbersDiagnostic");

            var uselessBackingFieldsDiagnostic = context.Diagnostics.FirstOrDefault(x => x.Id == "UselessBackingFieldsDiagnostic");

            if (magicNumbersDiagnostic != null)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: CodeFixResources.MagicNumbersFixTitle,
                        createChangedDocument: c => ChangeNumberAsync(context.Document, magicNumbersDiagnostic, root),
                        equivalenceKey: nameof(CodeFixResources.MagicNumbersFixTitle)),
                    magicNumbersDiagnostic);
            }
            if (uselessBackingFieldsDiagnostic != null)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: CodeFixResources.UselessBackingFieldsFixTitle,
                        createChangedSolution: c => MakeAutoPropertyAsync(context.Document, uselessBackingFieldsDiagnostic, context.CancellationToken),
                        equivalenceKey: nameof(CodeFixResources.UselessBackingFieldsFixTitle)),
                    uselessBackingFieldsDiagnostic);
            }
        }

        private async Task<Document> ChangeNumberAsync(Document document, Diagnostic diagnostic, SyntaxNode root)
        {
            var statement = root.FindNode(diagnostic.Location.SourceSpan).DescendantNodesAndSelf().FirstOrDefault(x => x.IsKind(SyntaxKind.NumericLiteralExpression));
            var firstMethodOrPropertyDeclaration = root.DescendantNodes().FirstOrDefault(node => node.IsKind(SyntaxKind.MethodDeclaration) || node.IsKind(SyntaxKind.PropertyDeclaration));
            var newFieldValue = statement.DescendantTokens().First().Text;
            var newFieldName = "_magicNumber" + diagnostic.Properties["MagicNumberCount"];
            var newFieldDeclaration = SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(SyntaxFactory.ParseTypeName("int"))
                .AddVariables(SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(newFieldName), null, SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(int.Parse(newFieldValue)))))))
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PrivateKeyword))
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.ConstKeyword));
            var classNode = root.DescendantNodes().FirstOrDefault(node => node.IsKind(SyntaxKind.ClassDeclaration));
            ClassDeclarationSyntax newClass = (ClassDeclarationSyntax)classNode;
            var newNodes = new List<SyntaxNode>();
            newNodes.Add(newFieldDeclaration);
            if (classNode is ClassDeclarationSyntax classDeclaration) {
                newClass = classDeclaration
                    .ReplaceNode(statement, SyntaxFactory.IdentifierName(newFieldName));
                    //.AddMembers(newFieldDeclaration)
                newClass = newClass.InsertNodesBefore(newClass.DescendantNodes().FirstOrDefault(node => node.IsKind(SyntaxKind.MethodDeclaration) || node.IsKind(SyntaxKind.PropertyDeclaration)), newNodes);
            }
            var newRoot = root
                .ReplaceNode(classNode, newClass);

            return await Task.FromResult(document.WithSyntaxRoot(newRoot));
        }

        private async Task<Solution> MakeAutoPropertyAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
        {
            var fieldLocation = diagnostic.Location;
            var propertyLocation = diagnostic.AdditionalLocations.First();

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var fieldSymbol = semanticModel.GetDeclaredSymbol(fieldLocation.SourceTree.GetRoot().FindNode(fieldLocation.SourceSpan));
            var propertySymbol = (IPropertySymbol)semanticModel.GetDeclaredSymbol(propertyLocation.SourceTree.GetRoot().FindNode(propertyLocation.SourceSpan));
            var originalSolution = document.Project.Solution;

            var newProperty = SyntaxFactory.PropertyDeclaration(SyntaxFactory.ParseTypeName(propertySymbol.Type.ToString().ToLower()), propertySymbol.Name)
                .AddModifiers(SyntaxFactory.ParseToken(propertySymbol.DeclaredAccessibility.ToString().ToLower()))
                .NormalizeWhitespace()
                .AddAccessorListAccessors(
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).AddModifiers(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)))
                .WithLeadingTrivia(propertyLocation.SourceTree.GetRoot().FindNode(propertyLocation.SourceSpan).GetLeadingTrivia())
                .WithTrailingTrivia(propertyLocation.SourceTree.GetRoot().FindNode(propertyLocation.SourceSpan).GetTrailingTrivia());

            var newSolution = await Renamer.RenameSymbolAsync(originalSolution, fieldSymbol, propertySymbol.Name, originalSolution.Workspace.Options, cancellationToken).ConfigureAwait(false);
            var newDocument = newSolution.GetDocument(document.Id);

            var syntaxTree = await newDocument.GetSyntaxTreeAsync();

            var documentEditor = await DocumentEditor.CreateAsync(newDocument);
            documentEditor.ReplaceNode(syntaxTree.GetRoot().FindNode(propertyLocation.SourceSpan), newProperty);
            documentEditor.RemoveNode(syntaxTree.GetRoot().FindNode(fieldLocation.SourceSpan));
            newDocument = documentEditor.GetChangedDocument();
            newSolution = newSolution.WithDocumentSyntaxRoot(newDocument.Id, await newDocument.GetSyntaxRootAsync());


            return await Task.FromResult(newSolution);
        }
    }
}
