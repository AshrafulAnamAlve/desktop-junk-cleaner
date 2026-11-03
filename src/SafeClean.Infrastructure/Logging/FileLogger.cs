using Microsoft.Extensions.Logging;

namespace SafeClean.Infrastructure.Logging;

internal sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) =>
        logLevel != LogLevel.None
        && (logLevel >= provider.MinimumLevel || (logLevel >= LogLevel.Information && FileLoggerProvider.IsAuditCategory(category)));

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(formatter);
        provider.Write(logLevel, category, formatter(state, exception), exception);
    }
}
