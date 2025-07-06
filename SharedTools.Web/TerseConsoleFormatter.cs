using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace SharedTestTools.Web;

public class TerseConsoleFormatter : ConsoleFormatter
{
    public TerseConsoleFormatter() : base("terse") { }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        var categoryName = logEntry.Category;

        // Shorten the category name - take only the last part after the last dot
        var shortName = categoryName.Split('.').LastOrDefault() ?? categoryName;
        if (shortName.EndsWith("Extensions", StringComparison.OrdinalIgnoreCase))
        {
            shortName = shortName[..^"Extensions".Length].TrimEnd();
        }

        textWriter.WriteLine($"{logEntry.LogLevel.ToString().ToLower()[..4]}: {shortName} - {message}");
    } 
}

public class TerseConsoleFormatterOptions : ConsoleFormatterOptions { }
