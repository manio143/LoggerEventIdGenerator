using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace LoggerEventIdGenerator
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class LoggerEventIdGeneratorAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId_EventIdZero = "LoggerEventIdZero";
        public const string DiagnosticId_EventIdDuplicated = "LoggerEventIdDuplicated";
        public const string ValuePropertyKey = "GeneratedValue";

        /// <summary>
        /// Mask for the low 8 bits (out of 32).
        /// 00000000000000000000000011111111
        /// </summary>
        private const uint LowBitMask = (1 << 8) - 1;

        /// <summary>
        /// Mask for the high 24 bits (out of 32).
        /// 11111111111111111111111100000000
        /// </summary>
        private const uint HighBitMask = unchecked((uint)(-1 << 8));

        // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
        // See https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Localizing%20Analyzers.md for more on localization
        private static readonly LocalizableString Title_EventIdZero = new LocalizableResourceString(nameof(Resources.EventIdZero_Title), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat_EventIdZero = new LocalizableResourceString(nameof(Resources.EventIdZero_Message), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description_EventIdZero = new LocalizableResourceString(nameof(Resources.EventIdZero_Description), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Title_EventIdDuplicated = new LocalizableResourceString(nameof(Resources.EventIdDuplicated_Title), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat_EventIdDuplicated = new LocalizableResourceString(nameof(Resources.EventIdDuplicated_Message), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description_EventIdDuplicated = new LocalizableResourceString(nameof(Resources.EventIdDuplicated_Description), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Logging";

        private static readonly DiagnosticDescriptor DiagnosticIdZero = new DiagnosticDescriptor(DiagnosticId_EventIdZero, Title_EventIdZero, MessageFormat_EventIdZero, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description_EventIdZero);
        private static readonly DiagnosticDescriptor DiagnosticIdDuplicated = new DiagnosticDescriptor(DiagnosticId_EventIdDuplicated, Title_EventIdDuplicated, MessageFormat_EventIdDuplicated, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description_EventIdDuplicated);

        private Dictionary<uint, ClassNumberRecord> CompilationEventIds = new Dictionary<uint, ClassNumberRecord>();

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(DiagnosticIdZero, DiagnosticIdDuplicated);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1026:Enable concurrent execution", Justification = "Because I need stable access to the dictionary of seen ids.")]
        public override void Initialize(AnalysisContext context)
        {
            // TODO: figure out how to handle situation where operation action doesn't take model action into account
            CompilationEventIds.Clear();

            // potentially we may need to get a max eventId off of generated code
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);

            context.RegisterSemanticModelAction(AnalyzeSemanticModel);
            context.RegisterOperationAction(AnalyzeInvocationOperation, OperationKind.Invocation);
        }

        private void AnalyzeSemanticModel(SemanticModelAnalysisContext context)
        {
            var model = context.SemanticModel;

            var eventIdType = model.Compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.EventId");
            if (eventIdType == null)
            {
                return; // the assembly doesn't have a reference to any logging
            }

            var args = FindAllEventIdLiteralArgs(model.SyntaxTree, model, eventIdType);
            foreach (var arg in args)
            {
                if (arg.Value == 0)
                {
                    continue;
                }

                int argEntryNum = (int)(arg.Value & LowBitMask);
                uint argClassNum = arg.Value & HighBitMask;

                if (!CompilationEventIds.TryGetValue(argClassNum, out var record))
                {
                    record = new ClassNumberRecord
                    {
                        MaxEntryValue = argEntryNum,
                        Arguments = new HashSet<ArgumentRecord>(),
                    };
                    CompilationEventIds.Add(argClassNum, record);
                }

                record.MaxEntryValue = Math.Max(record.MaxEntryValue, argEntryNum);
                record.Arguments.Add(arg);
            }

            CheckForDuplicatedEventIds(context);
        }

        private void CheckForDuplicatedEventIds(SemanticModelAnalysisContext context)
        {
            foreach (var classRecord in CompilationEventIds.Values)
            {
                var duplicatedIds = classRecord.Arguments.GroupBy(static r => r.Value).Where(static g => g.Count() > 1);
                foreach (var group in duplicatedIds)
                {
                    foreach (var arg in group)
                    {
                        var diagnostic = Diagnostic.Create(DiagnosticIdDuplicated, arg.Syntax.GetLocation(), arg.Syntax.GetText());

                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }

        private void AnalyzeInvocationOperation(OperationAnalysisContext context)
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

                        var args = FindAllEventIdLiteralArgs(encompassingType.SyntaxTree, model, eventIdType);

                        // args with 0
                        var emptyArgsSpans = args.Where(static arg => arg.Value == 0)
                            .Select(a => a.Location.SourceSpan).OrderBy(static x => x).ToList();

                        // highest existing id value in class
                        uint maxId = args.Max(static arg => arg.Value);

                        uint maxClassNum = maxId & HighBitMask;
                        int maxEntryNum = GetMaxEntryNumForCompilation(maxClassNum);

                        while (maxEntryNum == LowBitMask)
                        {
                            maxClassNum += LowBitMask + 1; // next HighBitMask number
                            maxEntryNum = GetMaxEntryNumForCompilation(maxClassNum);
                        }

                        uint newClassNum = (uint)MetroHash64.Run(typeName) & HighBitMask;
                        uint newEntryNum;
                        if (maxId == 0)
                        {
                            newEntryNum = (uint)emptyArgsSpans.IndexOf(arg.Syntax.GetLocation().SourceSpan);
                        }
                        else
                        {
                            newClassNum = maxClassNum;
                            newEntryNum = (uint)(maxEntryNum + emptyArgsSpans.IndexOf(arg.Syntax.GetLocation().SourceSpan) + 1);
                        }

                        var targetNewId = (int)(newClassNum + newEntryNum);

                        var properties = ImmutableDictionary<string, string>.Empty;
                        properties = properties.Add(ValuePropertyKey, targetNewId.ToString());
                        var diagnostic = Diagnostic.Create(DiagnosticIdZero, arg.Syntax.GetLocation(), properties);

                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }

        private int GetMaxEntryNumForCompilation(uint maxClassNum)
        {
            if (CompilationEventIds.TryGetValue(maxClassNum, out var record))
            {
                return record.MaxEntryValue;
            }

            return -1;
        }

        private static List<ArgumentRecord> FindAllEventIdLiteralArgs(SyntaxTree syntaxTree, SemanticModel model, INamedTypeSymbol eventIdType)
        {
            var argumentOperations = new List<ArgumentRecord>(32);

            foreach (var node in syntaxTree.GetRoot().DescendantNodes(static _ => true))
            {
                if (node is ArgumentSyntax argSyn)
                {
                    var argOp = model.GetOperation(argSyn) as IArgumentOperation;
                    if (argOp != null &&
                        argOp.Parameter.Type.Equals(eventIdType, SymbolEqualityComparer.Default) &&
                        TryGetLiteralValue(argOp.Syntax, out int value))
                    {
                        argumentOperations.Add(new ArgumentRecord
                        {
                            Operation = argOp,
                            Value = (uint)value,
                        });
                    }
                }
            }

            return argumentOperations;
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

        private struct ArgumentRecord
        {
            public uint Value { get; set; }
            public Location Location => Syntax.GetLocation();
            public SyntaxNode Syntax => Operation.Syntax;
            public IArgumentOperation Operation { get; set; }

            public override bool Equals(object obj) =>
                obj is ArgumentRecord rec && rec.Value == this.Value && rec.Location == this.Location;

            public override int GetHashCode() => this.Location.GetHashCode();
        }

        private class ClassNumberRecord
        {
            public int MaxEntryValue { get; set; }
            public HashSet<ArgumentRecord> Arguments { get; set; }
        }
    }
}
