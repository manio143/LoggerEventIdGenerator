using Microsoft.Extensions.Logging;

namespace ExampleProject
{
    internal partial class SecondClass
    {
        // should warn about duplicate
        [LoggerMessage(0x242eab01, LogLevel.Debug, "This is a debug log statement.")]
        public static partial void LogMe(ILogger logger);

    }
}
