using Microsoft.Extensions.Logging;

namespace ExampleProject
{
#pragma warning disable SYSLIB1006 // Multiple logging methods cannot use the same event id within a class
    public static partial class Extensions
    {
        [LoggerMessage(0, LogLevel.Debug, "This is a debug log statement.")]
        public static partial void MyDLog1(this ILogger logger);

        [LoggerMessage(0, LogLevel.Information, "This is an info log statement.")]
        public static partial void MyILog1(this ILogger logger);

        [LoggerMessage(EventId = 0, Level = LogLevel.Error, Message = "This is an error log statement.")]
        public static partial void MyELog(this ILogger logger);
    }

    public class Class
    {
        public void Run(ILogger<Class> logger)
        {
            logger.LogInformation(0x242eab00, "My inline message.");
            logger.LogInformation(0x242eab01, "My inline message.");
            logger.LogInformation(0, "My inline message.");
            logger.LogInformation(0, "My inline message.");
            logger.MyDLog1();
            logger.MyILog1();
        }
    }
}