using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace LoggerEventIdGenerator
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class LoggerEventIdGeneratorAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "LoggerEventIdGenerator";

        // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
        // See https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Localizing%20Analyzers.md for more on localization
        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.AnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Logging";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterOperationAction(AnalyzeSymbol, OperationKind.Invocation);
        }

        private static void AnalyzeSymbol(OperationAnalysisContext context)
        {
            var eventIdType = context.Compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.EventId");
            if (eventIdType == null)
            {
                return; // the assembly doesn't have a reference to any logging
            }
            var invocation = context.Operation as IInvocationOperation;

            foreach (var arg in invocation.Arguments)
            {
                if (arg.Parameter.Type.Equals(eventIdType, SymbolEqualityComparer.Default))
                {
                    // it's an int constant
                    if (arg.Syntax is ArgumentSyntax argSyn &&
                        argSyn.Expression is LiteralExpressionSyntax les &&
                        les.Token.Value.GetType() == typeof(int) &&
                        (int)les.Token.Value == 0)
                    {
                        var diagnostic = Diagnostic.Create(Rule, arg.Syntax.GetLocation());

                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }
    }
}
