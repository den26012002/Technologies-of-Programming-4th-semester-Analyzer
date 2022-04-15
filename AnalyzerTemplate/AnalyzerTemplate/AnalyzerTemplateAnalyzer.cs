using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.FindSymbols;

namespace AnalyzerTemplate
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class AnalyzerTemplateAnalyzer : DiagnosticAnalyzer
    {
        public const string MagicNumbersDiagnosticId = "MagicNumbersDiagnostic";
        public const string UselessBackingFieldsDiagnosticId = "UselessBackingFieldsDiagnostic";

        // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
        // See https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Localizing%20Analyzers.md for more on localization
        private static readonly LocalizableString MagicNumbersTitle = new LocalizableResourceString(nameof(Resources.MagicNumbersAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MagicNumbersMessageFormat = new LocalizableResourceString(nameof(Resources.MagicNumbersAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MagicNumbersDescription = new LocalizableResourceString(nameof(Resources.MagicNumbersAnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString UselessBackingFieldsTitle = new LocalizableResourceString(nameof(Resources.UselessBackingFieldsAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString UselessBackingFieldsMessageFormat = new LocalizableResourceString(nameof(Resources.UselessBackingFieldsAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString UselessBackingFieldsDescription = new LocalizableResourceString(nameof(Resources.UselessBackingFieldsAnalyzerDescription), Resources.ResourceManager, typeof(Resources));

        private const string Category = "Design";

        private static readonly DiagnosticDescriptor MagicNumbersRule = new DiagnosticDescriptor(MagicNumbersDiagnosticId, MagicNumbersTitle, MagicNumbersMessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: MagicNumbersDescription);
        private static readonly DiagnosticDescriptor UselessBackingFieldsRule = new DiagnosticDescriptor(UselessBackingFieldsDiagnosticId, UselessBackingFieldsTitle, UselessBackingFieldsMessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: UselessBackingFieldsDescription);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(MagicNumbersRule, UselessBackingFieldsRule); } }
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxTreeAction(AnalyzeMagicNumbers);
            context.RegisterSemanticModelAction(AnalyzeUselessBackingFields);
        }

        private static void AnalyzeMagicNumbers(SyntaxTreeAnalysisContext context)
        {
            var root = context.Tree.GetRoot(context.CancellationToken);
            AnalyzeMagicNumbers(context, root);
        }

        private static void AnalyzeMagicNumbers(SyntaxTreeAnalysisContext context, SyntaxNode root)
        {
            if (root.IsKind(SyntaxKind.FieldDeclaration))
            {
                return;
            }

            if (root.IsKind(SyntaxKind.NumericLiteralExpression))
            {
                ImmutableDictionary<string, string> properties = ImmutableDictionary.Create<string, string>();
                properties = properties.Add("MagicNumberCount", CountMagicFieldsNumber(context.Tree.GetRoot(context.CancellationToken)).ToString());
                var diagnostic = Diagnostic.Create(MagicNumbersRule, root.GetLocation(), properties);
                context.ReportDiagnostic(diagnostic);
            }

            foreach(var node in root.ChildNodes())
            {
                AnalyzeMagicNumbers(context, node);
            }
        }

        private static int CountMagicFieldsNumber(SyntaxNode root)
        {
            int ans = 0;
            foreach (var node in root.ChildNodes())
            {
                ans += CountMagicFieldsNumber(node);

                if (root.IsKind(SyntaxKind.FieldDeclaration) && IsFieldMagic(node))
                {
                    ++ans;
                }

            }

            return ans;
        }

        private static bool IsFieldMagic(SyntaxNode root)
        {
            bool ans = false;
            if (root.IsKind(SyntaxKind.VariableDeclarator))
            {
                Regex regex = new Regex(@"_magicNumber(\d)(\d*)");
                MatchCollection matchCollection = regex.Matches(root.ChildTokens().First().Text);
                if (matchCollection.Count > 0)
                {
                    ans = true;
                }
            }

            foreach (var node in root.ChildNodes())
            {
                if (IsFieldMagic(node))
                {
                    ans = true;
                }
            }

            return ans;
        }

        private static void AnalyzeUselessBackingFields(SemanticModelAnalysisContext context)
        {
            var root = context.SemanticModel.SyntaxTree.GetRoot();
            
            foreach (var node in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                var symbol = context.SemanticModel.GetDeclaredSymbol(node);
                if (symbol.SetMethod != null)
                {
                    continue;
                }

                var children = node.ChildNodes().OfType<ArrowExpressionClauseSyntax>();
                if (!children.Any())
                {
                    continue;
                }

                var getter = children.First();
                var getterChildren = getter.ChildNodes().OfType<IdentifierNameSyntax>();
                if (!getterChildren.Any())
                {
                    continue;
                }

                var backingFieldName = getterChildren.First();
                var backingField = context.SemanticModel.GetSymbolInfo(backingFieldName).Symbol as IFieldSymbol;

                if (backingField != null)
                {
                    var additionalLocations = new List<Location>();
                    additionalLocations.Add(node.GetLocation());
                    var diagnostic = Diagnostic.Create(UselessBackingFieldsRule, backingField.Locations.First(), additionalLocations);
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }

        private static IFieldSymbol GetBackingField(AccessorDeclarationSyntax getter, SemanticModel semanticModel)
        {
            if (getter == null)
            {
                return null;
            }

            if (getter.Body == null)
            {
                return null;
            }

            var statements = getter.Body.Statements;
            if (statements.Count != 1)
            {
                return null;
            }

            var returnStatement = statements.Single() as ReturnStatementSyntax;
            if (returnStatement == null || returnStatement.Expression == null)
            {
                return null;
            }

            return semanticModel.GetSymbolInfo(returnStatement.Expression).Symbol as IFieldSymbol;
        }
    }
}
