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
                        logger.LogInformation(-1571446272, "This is test");
                    }
                }
            }
            """;

            var expected = VerifyCS.Diagnostic("LoggerEventIdGenerator").WithLocation(0);
            await VerifyCS.VerifyCodeFixAsync(test, expected, replacement);
        }

        [TestMethod]
        public async Task CodeFix_For0_2LogStatements_GeneratesNewId()
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
                        logger.LogInformation(-1571446272, "This is test 1");
                        logger.LogInformation(-1571446271, "This is test 2");
                    }
                }
            }
            """;

            var expected = new[]
            {
                VerifyCS.Diagnostic("LoggerEventIdGenerator").WithLocation(0),
                VerifyCS.Diagnostic("LoggerEventIdGenerator").WithLocation(1),
            };
            await VerifyCS.VerifyCodeFixAsync(test, expected, replacement);
        }
    }
}
