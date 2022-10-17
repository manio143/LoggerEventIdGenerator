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
    /// <summary>
    /// Runs analysis on the semantic model of a C# file to detect all instances of arguments which hold
    /// integer literals that will be used as EventId.
    /// </summary>
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
            CompilationEventIds.Clear();

            // potentially we may need to get a max eventId off of generated code
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);

            context.RegisterSemanticModelAction(AnalyzeSemanticModel);
        }

        /// <summary>
        /// Runs analysis on the semantic model of a C# file to detect all instances of arguments which hold
        /// integer literals that will be used as EventId.
        /// </summary>
        private void AnalyzeSemanticModel(SemanticModelAnalysisContext context)
        {
            var model = context.SemanticModel;

            var attributeType = model.Compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.LoggerMessageAttribute");
            var eventIdType = model.Compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.EventId");
            if (eventIdType == null  /* not checking attribute because it may be null in older versions of library */)
            {
                return; // the assembly doesn't have a reference to any logging
            }

            var args = FindAllEventIdLiteralArgs(model, eventIdType, attributeType);
            var zeroArgs = new List<ArgumentRecord>(args.Count);
            foreach (var arg in args)
            {
                if (arg.Value == 0)
                {
                    zeroArgs.Add(arg);
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

            foreach (var arg in zeroArgs)
            {
                ProcessEventIdGenerationForArgument(context.SemanticModel.Compilation, context.ReportDiagnostic, arg.Syntax, args);
            }

            CheckForDuplicatedEventIds(context.ReportDiagnostic);
        }

        /// <summary>
        /// Reports diagnostics for arguments with duplicated eventId values in <see cref="CompilationEventIds"/>.
        /// </summary>
        private void CheckForDuplicatedEventIds(Action<Diagnostic> reportDiagnostic)
        {
            foreach (var classRecord in CompilationEventIds.Values)
            {
                var duplicatedIds = classRecord.Arguments.GroupBy(static r => r.Value).Where(static g => g.Count() > 1);
                foreach (var group in duplicatedIds)
                {
                    foreach (var arg in group)
                    {
                        var otherLocations = group.Except(new[] { arg }).Select(a => a.Location);
                        var diagnostic = Diagnostic.Create(DiagnosticIdDuplicated, arg.Syntax.GetLocation(), otherLocations, arg.Syntax.GetText());

                        reportDiagnostic(diagnostic);
                    }
                }
            }
        }

        /// <summary>
        /// Reports a diagnostic for argument <paramref name="node"/> with a generated value for a new eventId.
        /// </summary>
        private void ProcessEventIdGenerationForArgument(
            Compilation compilation,
            Action<Diagnostic> reportDiagnostic,
            SyntaxNode node,
            List<ArgumentRecord> args)
        {
            // args with 0
            var zeroArgsSpans = args.Where(static arg => arg.Value == 0)
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

            // when we have multiple 0 eventId values in the file
            // we precompute what would the generated value be if we applied all codefixes one by one from the top of the file
            var index = zeroArgsSpans.IndexOf(node.GetLocation().SourceSpan);
            if (index < 0)
            {
                throw new Exception("BUG: arg was not found on the list");
            }

            uint newClassNum;
            uint newEntryNum;
            if (maxId == 0)
            {
                string typeName = GetEncompassingTypeNameForSyntaxNode(compilation, node);
                newClassNum = (uint)MetroHash64.Run(typeName) & HighBitMask;
                newEntryNum = (uint)index;
            }
            else
            {
                newClassNum = maxClassNum;
                newEntryNum = (uint)(maxEntryNum + index + 1);
            }

            var targetNewId = (int)(newClassNum + newEntryNum);

            var properties = ImmutableDictionary<string, string>.Empty;
            properties = properties.Add(ValuePropertyKey, targetNewId.ToString());
            var diagnostic = Diagnostic.Create(DiagnosticIdZero, node.GetLocation(), properties);

            reportDiagnostic(diagnostic);
        }

        private static string GetEncompassingTypeNameForSyntaxNode(Compilation compilation, SyntaxNode node)
        {
            var encompassingType = node.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().First();
            var model = compilation.GetSemanticModel(encompassingType.SyntaxTree);
            var typeSymbol = model.GetDeclaredSymbol(encompassingType);
            var typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return typeName;
        }

        /// <summary>
        /// Gets max entry value from a <see cref="ClassNumberRecord"/> dictionary or <c>-1</c> if there is no record present.
        /// </summary>
        /// <param name="maxClassNum">Max class number (upper part of value, see <see cref="HighBitMask"/>).</param>
        private int GetMaxEntryNumForCompilation(uint maxClassNum)
        {
            if (CompilationEventIds.TryGetValue(maxClassNum, out var record))
            {
                return record.MaxEntryValue;
            }

            return -1;
        }

        /// <summary>
        /// Finds all instances of arguments in the semantic <paramref name="model"/> that represent logger EventIds.
        /// </summary>
        /// <param name="model">Semantic model (i.e. a semantic representation of a file).</param>
        /// <param name="eventIdType">Type symbol for <c>Microsoft.Extensions.Logging.EventId</c>.</param>
        /// <param name="attributeType">Type symbol for <c>Microsoft.Extensions.Logging.LoggerMessageAttribute</c>.</param>
        /// <returns>List of argument records.</returns>
        private static List<ArgumentRecord> FindAllEventIdLiteralArgs(SemanticModel model, INamedTypeSymbol eventIdType, INamedTypeSymbol attributeType)
        {
            var argumentOperations = new List<ArgumentRecord>(32);

            foreach (var node in model.SyntaxTree.GetRoot().DescendantNodes(static _ => true))
            {
                if (node is ArgumentSyntax argSyn)
                {
                    var argOp = model.GetOperation(argSyn) as IArgumentOperation;
                    if (argOp != null &&
                        argOp.Parameter.Type.Equals(eventIdType, SymbolEqualityComparer.Default) &&
                        TryGetLiteralValue(argOp.Syntax, out int value))
                    {
                        argumentOperations.Add(new ArgumentRecord((uint)value, argOp.Syntax));
                    }
                }
                else if (node is AttributeSyntax attr && attributeType != null)
                {
                    var type = model.GetTypeInfo(attr).Type;
                    if (type.Equals(attributeType, SymbolEqualityComparer.Default))
                    {
                        int index = 0;
                        foreach (var attrArgSyn in attr.ArgumentList.Arguments)
                        {
                            const string expectedParameterName = "EventId";
                            var parameterName = (attrArgSyn.NameEquals?.Name ?? attrArgSyn.NameColon?.Name)?.Identifier.Text;
                            if ((expectedParameterName.Equals(parameterName, StringComparison.OrdinalIgnoreCase) || (parameterName is null && index == 0)) &&
                                TryGetLiteralValue(attrArgSyn, out int value))
                            {
                                argumentOperations.Add(new ArgumentRecord((uint)value, attrArgSyn));
                            }
                        }
                    }
                }
            }

            return argumentOperations;
        }

        /// <summary>
        /// Try to get the <see cref="int"/> value out of a <see cref="LiteralExpressionSyntax"/> inside an argument <paramref name="node"/>.
        /// </summary>
        /// <param name="node">Argument node (<see cref="ArgumentSyntax"/> or <see cref="AttributeArgumentSyntax"/>).</param>
        /// <param name="value">Outout value from literal passed as argument.</param>
        /// <returns><c>true</c> if literal has been found, <c>false</c> otherwise.</returns>
        private static bool TryGetLiteralValue(SyntaxNode node, out int value)
        {
            ExpressionSyntax expression = null;
            if (node is ArgumentSyntax argSyn)
            {
                expression = argSyn.Expression;
            }
            else if (node is AttributeArgumentSyntax attrArgSyn)
            {
                expression= attrArgSyn.Expression;
            }

            if (expression != null)
            {
                // positive int
                if (expression is LiteralExpressionSyntax les &&
                    les.Token.Value.GetType() == typeof(int))
                {
                    value = (int)les.Token.Value;
                    return true;
                }
                // negative int
                // note this isn't handling int.MinValue edge case
                else if (expression is PrefixUnaryExpressionSyntax pues &&
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

        /// <summary>
        /// Container for an argument syntax node and its value.
        /// </summary>
        private struct ArgumentRecord
        {
            public uint Value { get; }
            public Location Location => Syntax.GetLocation();
            public SyntaxNode Syntax { get; }

            public ArgumentRecord(uint value, SyntaxNode syntax)
            {
                this.Value = value;
                this.Syntax = syntax;
            }

            public override bool Equals(object obj) =>
                obj is ArgumentRecord rec && rec.Value == this.Value && rec.Location == this.Location;

            public override int GetHashCode() => this.Location.GetHashCode();
        }

        /// <summary>
        /// A helper class holding arguments with the same upper part (see <see cref="HighBitMask"/>) of the value.
        /// </summary>
        private class ClassNumberRecord
        {
            /// <summary>
            /// Maximum value of <see cref="Arguments"/> in the lower part (see <see cref="LowBitMask"/>) of the values.
            /// </summary>
            public int MaxEntryValue { get; set; }

            /// <summary>
            /// Set of arguments with the same upper part (see <see cref="HighBitMask"/>) of the value.
            /// </summary>
            public HashSet<ArgumentRecord> Arguments { get; set; }
        }
    }
}
