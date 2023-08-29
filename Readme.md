# LoggerEventIdGenerator

[![NuGet version (LoggerEventIdGenerator)](https://img.shields.io/nuget/v/LoggerEventIdGenerator.svg)](https://www.nuget.org/packages/LoggerEventIdGenerator/)

A small analyzer + code fix, that is meant to allow for easy generation of event Ids for your log statements.

![CodeFix to generate eventId on a LoggerMessage attribute](https://raw.githubusercontent.com/manio143/LoggerEventIdGenerator/master/docs/images/codefix_screenshot.png)

## Details

### Problem
When using `Microsoft.Extensions.Logging` library you may have seen the `EventId` parameter on the `ILogger` extension methods.
Event ids are useful to map the log statement in your log viewer (such as [Seq](https://datalust.co/seq)) back to the code.
They need to be persistent and committed to your repo.
They should be unique.

From what I've seen online most people don't set it at all (keep it zero for all logs) or suggest setting it from an enum,
which needs to be manually updated when adding a log statement. But this can lead to slowdown during development.

### Solution
Provide a tool that automatically generates pseudo-random event ids for your log statements.

I decided to implement in the form of a C# analyzer, because it provides the tooling to inspect code in a structured way,
rather than doing regex or some other funky text analysis.
I'm leveraging the CodeFix mechanism to allow developers to quickly generate the new event ids inline when writing the log statement.

### Generation algorithm
If there's no previous event ids in the file, when generating the first one we will get a pseudo random value by
hashing the fully qualified name of the encompassing class. From that we will increment the value for the following log statements.

The event id is separated into two parts: upper three bytes are the class hash, low byte is a sequential number.

When a file has more than 256 log statements it will roll over to the next upper value.

### Supported patterns
The main supported styles of logging:

```csharp
using Microsoft.Extensions.Logging;

partial class TestClass
{
	private ILogger logger;

	[LoggerMessage(0, LogLevel.Debug, "This is a debug log statement.")]
	partial void LogMyDebug();

	[LoggerMessage(Level = LogLevel.Information, Message = "This is a info log statement.", EventId = 0)]
	partial void LogMyInfo();

	public void Run()
	{
		logger.LogWarning(0, "This is a warning log statement.");
	}
}
```

After generation:

```csharp
    [LoggerMessage(-0x52bdd600, LogLevel.Debug, "This is a debug log statement.")]
    partial void LogMyDebug();

    [LoggerMessage(Level = LogLevel.Information, Message = "This is a info log statement.", EventId = -0x52bdd5ff)]
    partial void LogMyInfo();

    public void Run()
    {
        logger.LogWarning(-0x52bdd5fe, "This is a warning log statement.");
    }
```

### Detecting duplicates
Additionally I'm trying to detect duplicates among persisted event ids. An especially common pattern is copying code from
somewhere else and not changing the event id in the log statements. Now it will emit a warning.
Though it may not always emit the warning in the newer place, because roslyn doesn't have an insight into for example git blame.

### Automating id generation
To make it even more convenient you can set up a [pre-commit hook](https://githooks.com/) to run the codefix on your projects automatically.

```
dotnet format analyzers --diagnostics="LoggerEventIdZero"
```

## License
I'm releasing it under MIT license. Contributions welcome.

## Building
Build the solution. The `LoggerEventIdGenerator.Package` project emits a NuGet package with the analyzer and codefix.
