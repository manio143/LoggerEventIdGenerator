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
        //No diagnostics expected to show up
        [TestMethod]
        public async Task TestMethod1()
        {
            var test = @"";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        //Diagnostic and CodeFix both triggered and checked for
        [TestMethod]
        public async Task TestMethod2()
        {
            var test = """
            using System;
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

            var expected = VerifyCS.Diagnostic("LoggerEventIdGenerator").WithLocation(0);
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }
    }
}
