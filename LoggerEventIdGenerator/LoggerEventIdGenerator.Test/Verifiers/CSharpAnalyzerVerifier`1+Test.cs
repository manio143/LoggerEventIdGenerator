using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace LoggerEventIdGenerator.Test
{
    public static partial class CSharpAnalyzerVerifier<TAnalyzer>
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        public class Test : CSharpAnalyzerTest<TAnalyzer, MSTestVerifier>
        {
            public Test()
            {
                SolutionTransforms.Add((solution, projectId) =>
                {
                    var compilationOptions = solution.GetProject(projectId).CompilationOptions
                        .WithOutputKind(OutputKind.DynamicallyLinkedLibrary)
                        .WithSpecificDiagnosticOptions(
                            solution.GetProject(projectId).CompilationOptions.SpecificDiagnosticOptions
                                .SetItems(CSharpVerifierHelper.NullableWarnings));
                    solution = solution.WithProjectCompilationOptions(projectId, compilationOptions)
                        .WithProjectMetadataReferences(projectId, new[]
                        {
                            // System.Runtime.dll
                            MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location.Replace("Private.CoreLib", "Runtime")),
                            // System.Private.CoreLib.dll
                            MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location),
                            // Microsoft.Extensions.Logging.Abstractions.dll
                            MetadataReference.CreateFromFile(typeof(ILogger).GetTypeInfo().Assembly.Location)
                        });

                    return solution;
                });
            }
        }
    }
}
