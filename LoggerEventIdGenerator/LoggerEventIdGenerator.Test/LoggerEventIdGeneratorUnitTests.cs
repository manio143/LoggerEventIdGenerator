using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using VerifyCS = LoggerEventIdGenerator.Test.CSharpCodeFixVerifier<
    LoggerEventIdGenerator.LoggerEventIdGeneratorAnalyzer,
    LoggerEventIdGenerator.LoggerEventIdGeneratorCodeFixProvider>;

namespace LoggerEventIdGenerator.Test
{
    [TestClass]
    public class LoggerEventIdGeneratorUnitTest
    {
        [TestMethod]
        public async Task CodeFix_For0_GeneratesNewId()
        {
            var test = """
            using Microsoft.Extensions.Logging;

            namespace ConsoleApplication1
            {
                class A
                {  
                    public static void X()
                    {
                        ILogger logger = null;
                        logger.LogInformation({|#0:0|}, "This is test");
                    }
                }
            }
            """;
            var replacement = """
            using Microsoft.Extensions.Logging;

            namespace ConsoleApplication1
            {
                class A
                {  
                    public static void X()
                    {
                        ILogger logger = null;
                        logger.LogInformation(-0x5daa5e00, "This is test");
                    }
                }
            }
            """;

            var expected = VerifyCS.Diagnostic(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdZero).WithLocation(0);
            await VerifyCS.VerifyCodeFixAsync(test, expected, replacement);
        }

        [TestMethod]
        public async Task CodeFix_For0_2LogStatements_Both0_GeneratesNewId()
        {
            var test = """
            using Microsoft.Extensions.Logging;

            namespace ConsoleApplication1
            {
                class A
                {  
                    public static void X()
                    {
                        ILogger logger = null;
                        logger.LogInformation({|#0:0|}, "This is test 1");
                        logger.LogInformation({|#1:0|}, "This is test 2");
                    }
                }
            }
            """;
            var replacement = """
            using Microsoft.Extensions.Logging;

            namespace ConsoleApplication1
            {
                class A
                {  
                    public static void X()
                    {
                        ILogger logger = null;
                        logger.LogInformation(-0x5daa5e00, "This is test 1");
                        logger.LogInformation(-0x5daa5dff, "This is test 2");
                    }
                }
            }
            """;

            var expected = new[]
            {
                VerifyCS.Diagnostic(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdZero).WithLocation(0),
                VerifyCS.Diagnostic(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdZero).WithLocation(1),
            };
            await VerifyCS.VerifyCodeFixAsync(test, expected, replacement);
        }

        [TestMethod]
        public async Task CodeFix_For0_2LogStatements_1AlreadySetToGeneratedValue_GeneratesNewId()
        {
            var test = """
            using Microsoft.Extensions.Logging;

            namespace ConsoleApplication1
            {
                class A
                {  
                    public static void X()
                    {
                        ILogger logger = null;
                        logger.LogInformation(-0x5daa5e00, "This is test 1");
                        logger.LogInformation({|#0:0|}, "This is test 2");
                    }
                }
            }
            """;
            var replacement = """
            using Microsoft.Extensions.Logging;

            namespace ConsoleApplication1
            {
                class A
                {  
                    public static void X()
                    {
                        ILogger logger = null;
                        logger.LogInformation(-0x5daa5e00, "This is test 1");
                        logger.LogInformation(-0x5daa5dff, "This is test 2");
                    }
                }
            }
            """;

            var expected = VerifyCS.Diagnostic(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdZero).WithLocation(0);
            await VerifyCS.VerifyCodeFixAsync(test, expected, replacement);
        }

        [TestMethod]
        public async Task CodeFix_For0_2LogStatements_1AlreadySetToCustomValue_GeneratesNewId()
        {
            var test = """
            using Microsoft.Extensions.Logging;

            namespace ConsoleApplication1
            {
                class A
                {  
                    public static void X()
                    {
                        ILogger logger = null;
                        logger.LogInformation(0x64, "This is test 1");
                        logger.LogInformation({|#0:0|}, "This is test 2");
                    }
                }
            }
            """;
            var replacement = """
            using Microsoft.Extensions.Logging;

            namespace ConsoleApplication1
            {
                class A
                {  
                    public static void X()
                    {
                        ILogger logger = null;
                        logger.LogInformation(0x64, "This is test 1");
                        logger.LogInformation(0x65, "This is test 2");
                    }
                }
            }
            """;

            var expected = VerifyCS.Diagnostic(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdZero).WithLocation(0);
            await VerifyCS.VerifyCodeFixAsync(test, expected, replacement);
        }

        [TestMethod]
        public async Task CodeFix_For0_2LogStatements_1AlreadySetToMinValueMinus1_GeneratesMinus1()
        {
            // We can deal with int.MinValue event Id because it has no Abs form, so I opted to return -1 instead - this is a rare thing anyways
            var test = $$"""
            using Microsoft.Extensions.Logging;

            namespace ConsoleApplication1
            {
                class A
                {
                    public static void X()
                    {
                        ILogger logger = null;
                        logger.LogInformation({{unchecked(int.MinValue - 1)}}, "This is test 1");
                        logger.LogInformation({|#0:0|}, "This is test 2");
                    }
                }
            }
            """;
            var replacement = """
            using Microsoft.Extensions.Logging;

            namespace ConsoleApplication1
            {
                class A
                {
                    public static void X()
                    {
                        ILogger logger = null;
                        logger.LogInformation(2147483647, "This is test 1");
                        logger.LogInformation(-0x1, "This is test 2");
                    }
                }
            }
            """;

            var expected = VerifyCS.Diagnostic(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdZero).WithLocation(0);
            await VerifyCS.VerifyCodeFixAsync(test, expected, replacement);
        }

        [TestMethod]
        public async Task CodeFix_For0_ManyLogStatements_OverflowMinusOne_GeneratesNewIds()
        {
            var test = """
            using Microsoft.Extensions.Logging;

            namespace ConsoleApplication1
            {
                class A
                {  
                    public static void X()
                    {
                        ILogger logger = null;
                        logger.LogInformation(0x64, "This is test 1");
                        logger.LogInformation(0xafe, "This is test 2");
                        logger.LogInformation({|#0:0|}, "This is test 3");
                        logger.LogInformation({|#1:0|}, "This is test 4");
                    }
                }
            }
            """;
            var replacement = """
            using Microsoft.Extensions.Logging;

            namespace ConsoleApplication1
            {
                class A
                {  
                    public static void X()
                    {
                        ILogger logger = null;
                        logger.LogInformation(0x64, "This is test 1");
                        logger.LogInformation(0xafe, "This is test 2");
                        logger.LogInformation(0xaff, "This is test 3");
                        logger.LogInformation(0xb00, "This is test 4");
                    }
                }
            }
            """;

            var expected = new[]
            {
                VerifyCS.Diagnostic(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdZero).WithLocation(0),
                VerifyCS.Diagnostic(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdZero).WithLocation(1),
            };
            await VerifyCS.VerifyCodeFixAsync(test, expected, replacement);
        }

        [TestMethod]
        public async Task CodeFix_For0_ManyLogStatements_Overflow_GeneratesNewIds()
        {
            var test = """
            using Microsoft.Extensions.Logging;

            namespace ConsoleApplication1
            {
                class A
                {  
                    public static void X()
                    {
                        ILogger logger = null;
                        logger.LogInformation(0x64, "This is test 1");
                        logger.LogInformation(0xaff, "This is test 2");
                        logger.LogInformation({|#0:0|}, "This is test 3");
                        logger.LogInformation({|#1:0|}, "This is test 4");
                    }
                }
            }
            """;
            var replacement = """
            using Microsoft.Extensions.Logging;

            namespace ConsoleApplication1
            {
                class A
                {  
                    public static void X()
                    {
                        ILogger logger = null;
                        logger.LogInformation(0x64, "This is test 1");
                        logger.LogInformation(0xaff, "This is test 2");
                        logger.LogInformation(0xb00, "This is test 3");
                        logger.LogInformation(0xb01, "This is test 4");
                    }
                }
            }
            """;

            var expected = new[]
            {
                VerifyCS.Diagnostic(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdZero).WithLocation(0),
                VerifyCS.Diagnostic(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdZero).WithLocation(1),
            };
            await VerifyCS.VerifyCodeFixAsync(test, expected, replacement);
        }

        [TestMethod]
        public async Task CodeFix_For0_InAttribute_Ctor_GeneratesNewIds()
        {
            var test = """
            using Microsoft.Extensions.Logging;

            namespace ExampleProject
            {
                public static partial class Extensions
                {
                    [LoggerMessage({|#0:0|}, LogLevel.Debug, "This is a debug log statement.")]
                    static partial void Do(this ILogger logger);
                }
            }
            """;
            var replacement = """
            using Microsoft.Extensions.Logging;

            namespace ExampleProject
            {
                public static partial class Extensions
                {
                    [LoggerMessage(0x3b3e7c00, LogLevel.Debug, "This is a debug log statement.")]
                    static partial void Do(this ILogger logger);
                }
            }
            """;

            var expected = VerifyCS.Diagnostic(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdZero).WithLocation(0);
            await VerifyCS.VerifyCodeFixAsync(test, expected, replacement);
        }

        [TestMethod]
        public async Task CodeFix_For0_InAttribute_CtorNamed_GeneratesNewIds()
        {
            var test = """
            using Microsoft.Extensions.Logging;

            namespace ExampleProject
            {
                public static partial class Extensions
                {
                    [LoggerMessage({|#0:eventId: 0|}, level: LogLevel.Debug, message: "This is a debug log statement.")]
                    static partial void Do(this ILogger logger);
                }
            }
            """;
            var replacement = """
            using Microsoft.Extensions.Logging;

            namespace ExampleProject
            {
                public static partial class Extensions
                {
                    [LoggerMessage(eventId: 0x3b3e7c00, level: LogLevel.Debug, message: "This is a debug log statement.")]
                    static partial void Do(this ILogger logger);
                }
            }
            """;

            var expected = VerifyCS.Diagnostic(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdZero).WithLocation(0);
            await VerifyCS.VerifyCodeFixAsync(test, expected, replacement);
        }

        [TestMethod]
        public async Task CodeFix_For0_InAttribute_Property_GeneratesNewIds()
        {
            var test = """
            using Microsoft.Extensions.Logging;

            namespace ExampleProject
            {
                public static partial class Extensions
                {
                    [LoggerMessage(Level = LogLevel.Error, {|#0:EventId = 0|}, Message = "This is an error log statement.")]
                    static partial void Do(this ILogger logger);
                }
            }
            """;
            var replacement = """
            using Microsoft.Extensions.Logging;

            namespace ExampleProject
            {
                public static partial class Extensions
                {
                    [LoggerMessage(Level = LogLevel.Error, EventId = 0x3b3e7c00, Message = "This is an error log statement.")]
                    static partial void Do(this ILogger logger);
                }
            }
            """;

            var expected = VerifyCS.Diagnostic(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdZero).WithLocation(0);
            await VerifyCS.VerifyCodeFixAsync(test, expected, replacement);
        }

        [TestMethod]
        public async Task Analyzer_ForDupe_2LogStatements_BothSameId_ReportsDiagnostic()
        {
            var test = """
            using Microsoft.Extensions.Logging;

            namespace ConsoleApplication1
            {
                class A
                {  
                    public static void X()
                    {
                        ILogger logger = null;
                        logger.LogInformation({|#0:0x01|}, "This is test 1");
                        logger.LogInformation({|#1:0x01|}, "This is test 2");
                    }
                }
            }
            """;

            var expected = new[]
            {
                VerifyCS.Diagnostic(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdDuplicated).WithLocation(0).WithArguments("0x01"),
                VerifyCS.Diagnostic(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdDuplicated).WithLocation(1).WithArguments("0x01"),
            };
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [TestMethod]
        public async Task Analyzer_ForDupe_4LogStatements_2WithSameId_ReportsDiagnostic()
        {
            var test = """
            using Microsoft.Extensions.Logging;

            namespace ConsoleApplication1
            {
                class A
                {  
                    public static void X()
                    {
                        ILogger logger = null;
                        logger.LogInformation({|#0:0x01|}, "This is test 1");
                        logger.LogInformation(0x02, "This is test 2");
                        logger.LogInformation({|#1:0x01|}, "This is test 3");
                        logger.LogInformation(0x03, "This is test 4");
                    }
                }
            }
            """;

            var expected = new[]
            {
                VerifyCS.Diagnostic(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdDuplicated).WithLocation(0).WithArguments("0x01"),
                VerifyCS.Diagnostic(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdDuplicated).WithLocation(1).WithArguments("0x01"),
            };
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [TestMethod]
        public async Task Analyzer_ForDupe_LogStatementsAcrossClasses_2WithSameId_ReportsDiagnostic()
        {
            var test = """
            using Microsoft.Extensions.Logging;

            namespace ConsoleApplication1
            {
                class A
                {  
                    public static void X()
                    {
                        ILogger logger = null;
                        logger.LogInformation({|#0:0x01|}, "This is test 1");
                    }
                }
            }
            namespace ConsoleApplication2
            {
                class B
                {  
                    public static void Y()
                    {
                        ILogger logger = null;
                        logger.LogInformation({|#1:0x01|}, "This is test 2");
                    }
                }
            }
            """;

            var expected = new[]
            {
                VerifyCS.Diagnostic(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdDuplicated).WithLocation(0).WithArguments("0x01"),
                VerifyCS.Diagnostic(LoggerEventIdGeneratorAnalyzer.DiagnosticId_EventIdDuplicated).WithLocation(1).WithArguments("0x01"),
            };
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }
    }
}
