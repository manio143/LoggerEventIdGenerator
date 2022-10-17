using Microsoft.Extensions.Logging;

partial class TestClass
{
    private ILogger logger;

    [LoggerMessage(-0x52bdd600, LogLevel.Debug, "This is a debug log statement.")]
    partial void LogMyDebug();

    [LoggerMessage(Level = LogLevel.Information, Message = "This is a info log statement.", EventId = -0x52bdd5ff)]
    partial void LogMyInfo();

    public void Run()
    {
        logger.LogWarning(-0x52bdd5fe, "This is a warning log statement.");
    }
}