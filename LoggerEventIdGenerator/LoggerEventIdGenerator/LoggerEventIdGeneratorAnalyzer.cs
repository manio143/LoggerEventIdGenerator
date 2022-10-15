using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;

namespace LoggerEventIdGenerator
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class LoggerEventIdGeneratorAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "LoggerEventIdGenerator";
        public const string ValuePropertyKey = "GeneratedValue";

        // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
        // See https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Localizing%20Analyzers.md for more on localization
        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.AnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Logging";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        private HashSet<int> takenIds = new HashSet<int>();

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterOperationAction(AnalyzeSymbol, OperationKind.Invocation);
        }

        private void AnalyzeSymbol(OperationAnalysisContext context)
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
                    if (TryGetLiteralValue(arg.Syntax, out int value) && value == 0)
                    {
                        var encompassingType = invocation.Syntax.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().First();
                        var model = context.Compilation.GetSemanticModel(encompassingType.SyntaxTree);
                        var typeSymbol = model.GetDeclaredSymbol(encompassingType);
                        var typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                        // all int EventId args in this class
                        var args = encompassingType.SyntaxTree.GetRoot().DescendantNodes(_ => true).OfType<ArgumentSyntax>()
                            .Select(argSyn => model.GetOperation(argSyn) as IArgumentOperation)
                            .Where(argOp => argOp != null &&
                                    argOp.Parameter.Type.Equals(eventIdType, SymbolEqualityComparer.Default) &&
                                    TryGetLiteralValue(argOp.Syntax, out _))
                            .OrderBy(argOp => argOp.Syntax.GetLocation().SourceSpan)
                            .ToList();

                        // args with 0
                        var emptyArgsSpans = args.Where(argOp => GetLiteralValue(argOp.Syntax) == 0)
                            .Select(a => a.Syntax.GetLocation().SourceSpan).ToList();
                        
                        // prevent duplicate ids
                        // NOTE: not sure how well this will work tbh...
                        foreach (var argVal in args.Select(argOp => GetLiteralValue(argOp.Syntax)).Where(val => val != 0))
                        {
                            takenIds.Add(argVal);
                        }

                        // highest existing id value in class
                        var maxId = args.Max(argOp => GetLiteralValue(argOp.Syntax));

                        var maxEntryNum = (uint)maxId & ((1 << 8) - 1); // 8 bit mask (low)
                        var maxClassNum = (uint)maxId & (-1 << 8); // 24 bit mask (high)

                        var newClassNum = (uint)MetroHash64.Run(typeName) & (-1 << 8);
                        uint newEntryNum;
                        if (maxId == 0)
                        {
                            newEntryNum = (uint)emptyArgsSpans.IndexOf(arg.Syntax.GetLocation().SourceSpan);
                        }
                        else if (maxClassNum == newClassNum) // normal case for under 256 log statements per class
                        {
                            newEntryNum = maxEntryNum + (uint)emptyArgsSpans.IndexOf(arg.Syntax.GetLocation().SourceSpan) + 1u;
                        }
                        else
                        {
                            newClassNum = maxEntryNum;
                            newEntryNum = maxEntryNum + (uint)emptyArgsSpans.IndexOf(arg.Syntax.GetLocation().SourceSpan) + 1u;
                        }

                        var targetNewId = (int)(newClassNum + newEntryNum);

                        // TODO: find all other eventIds in the project and verify there's no colision
                        // TODO: if id is taken we should find a new max in it's class and go from there
                        // FIXME: this currently breaks batch fixups for edge cases
                        while (takenIds.Contains(targetNewId))
                            targetNewId++;

                        var properties = ImmutableDictionary<string, string>.Empty;
                        properties = properties.Add(ValuePropertyKey, targetNewId.ToString());
                        var diagnostic = Diagnostic.Create(Rule, arg.Syntax.GetLocation(), properties);

                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }

        private static int GetLiteralValue(SyntaxNode node)
        {
            if (TryGetLiteralValue(node, out int value))
            {
                return value;
            }

            return default;
        }

        private static bool TryGetLiteralValue(SyntaxNode node, out int value)
        {
            if (node is ArgumentSyntax argSyn)
            {
                // positive int
                if(argSyn.Expression is LiteralExpressionSyntax les &&
                    les.Token.Value.GetType() == typeof(int))
                {
                    value = (int)les.Token.Value;
                    return true;
                }
                // negative int
                else if (argSyn.Expression is PrefixUnaryExpressionSyntax pues &&
                    pues.ChildNodes().First() is LiteralExpressionSyntax les2 &&
                    les2.Token.Value.GetType() == typeof(int))
                {
                    value = -1 * (int)les2.Token.Value;
                    return true;
                }
            }
            value = 0;
            return false;
        }
    }
}
