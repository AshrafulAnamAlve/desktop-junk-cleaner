using Microsoft.Extensions.Logging;
using SafeClean.Core.Interfaces;

namespace SafeClean.Infrastructure.Tests.Cleanup;

internal sealed class FakeElevationProbe(bool isElevated) : IElevationProbe
{
    public bool IsElevated => isElevated;
}

/// <summary>A clock set by the test, for checking how old a scan may be.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Keeps every formatted log message, so tests can check exactly what the audit log would contain.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_messages)
        {
            _messages.Add(formatter(state, exception));
        }
    }
}
