using NuGet.Common;

namespace SharedTools.Web.Modules;

/// <summary>
/// An adapter to pass log messages from the NuGet client libraries
/// into the standard Microsoft.Extensions.Logging infrastructure.
/// </summary>
public class NugetLoggerAdapter : NuGet.Common.ILogger
{
    private readonly Microsoft.Extensions.Logging.ILogger MicrosoftLogger;

    public NugetLoggerAdapter(Microsoft.Extensions.Logging.ILogger logger)
    {
        MicrosoftLogger = logger;
    }

    public void Log(LogLevel level, string data)
    {
        MicrosoftLogger.Log(
            TranslateLevel(level),
            0, // EventId
            data, // State
            null, // Exception
            (state, _) => $"[NuGet] {state}" // Formatter function
        );
    }

    public void Log(ILogMessage message)
    {
        MicrosoftLogger.Log(
            TranslateLevel(message.Level),
            0, // EventId
            message, // State
            null, // Exception
            (state, _) => $"[NuGet:{state.Code}] {state.FormatWithCode()}" // Formatter function
        );
    }

    public Task LogAsync(LogLevel level, string data)
    {
        Log(level, data);
        return Task.CompletedTask;
    }

    public Task LogAsync(ILogMessage message)
    {
        Log(message);
        return Task.CompletedTask;
    }

    public void LogDebug(string data) => Log(LogLevel.Debug, data);
    public void LogVerbose(string data) => Log(LogLevel.Verbose, data);
    public void LogInformation(string data) => Log(LogLevel.Information, data);
    public void LogMinimal(string data) => Log(LogLevel.Minimal, data);
    public void LogWarning(string data) => Log(LogLevel.Warning, data);
    public void LogError(string data) => Log(LogLevel.Error, data);
    public void LogInformationSummary(string data) => Log(LogLevel.Information, data);

    /// <summary>
    /// Translates NuGet's log level to the corresponding Microsoft log level.
    /// </summary>
    private static Microsoft.Extensions.Logging.LogLevel TranslateLevel(LogLevel nugetLevel)
    {
        return nugetLevel switch
        {
            LogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
            LogLevel.Verbose => Microsoft.Extensions.Logging.LogLevel.Trace, // Verbose is most detailed
            LogLevel.Information => Microsoft.Extensions.Logging.LogLevel.Information,
            LogLevel.Minimal => Microsoft.Extensions.Logging.LogLevel.Information,
            LogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
            LogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
            _ => Microsoft.Extensions.Logging.LogLevel.None
        };
    }
}