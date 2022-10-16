using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LoggerEventIdGenerator
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LoggerEventIdGeneratorCodeFixProvider)), Shared]
    public class LoggerEventIdGeneratorCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdZero); }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            // See https://github.com/dotnet/roslyn/blob/main/docs/analyzers/FixAllProvider.md for more information on Fix All Providers
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;
            var eventId = int.TryParse(diagnostic.Properties[LoggerEventIdGeneratorAnalyzer.ValuePropertyKey], out int value) ? value : -1;

            // Find the type declaration identified by the diagnostic.
            var declaration = root.FindToken(diagnosticSpan.Start).Parent.AncestorsAndSelf().OfType<LiteralExpressionSyntax>().First();

            // Register a code action that will invoke the fix.
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: CodeFixResources.CodeFixTitle,
                    createChangedDocument: c => SetNewEventId(context.Document, declaration, eventId, c),
                    equivalenceKey: nameof(CodeFixResources.CodeFixTitle)),
                diagnostic);
        }

        private async Task<Document> SetNewEventId(Document document, SyntaxNode node, int eventId, CancellationToken cancellationToken)
        {
            if (eventId == int.MinValue)
            {
                // special case - what are the odds?
                // otherwise Math.Abs throws about overflow
                eventId = -1;
            }

            var literalValue = Math.Abs(eventId);
            var hexNumber = $"0x{Convert.ToString(literalValue, toBase: 16)}";
            var literal = SyntaxFactory.Literal(hexNumber, literalValue);
            var literalExpression = SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, literal);
            
            var expression = eventId >= 0
                ? (SyntaxNode)literalExpression
                : SyntaxFactory.PrefixUnaryExpression(
                    SyntaxKind.UnaryMinusExpression,
                    SyntaxFactory.Token(SyntaxKind.MinusToken),
                    literalExpression);

            var originalRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var newRoot = originalRoot.ReplaceNode(node, expression);

            return document.WithSyntaxRoot(newRoot);
        }
    }
}
