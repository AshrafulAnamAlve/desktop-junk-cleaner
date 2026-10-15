using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SafeClean.Infrastructure.Logging;

/// <summary>
/// Minimal local file logger: one UTF-8 text file per day in the given directory.
/// Logging must never crash the app, so I/O failures disable file logging instead of throwing.
/// Nothing is ever sent over the network.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string? _profilePath;
    private StreamWriter? _writer;
    private DateOnly _writerDay;
    private bool _disabled;
    private bool _disposed;

    public FileLoggerProvider(string directory, Func<DateTimeOffset>? clock = null, string? profilePathToRedact = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _profilePath = string.IsNullOrWhiteSpace(profilePathToRedact)
            ? System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile)
            : profilePathToRedact;
    }

    /// <summary>%LOCALAPPDATA%\SafeClean\Logs — deliberately outside the Temp folder SafeClean cleans.</summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "SafeClean",
        "Logs");

    public string Directory => _directory;

    /// <summary>True after an I/O failure turned file logging off for the rest of the session.</summary>
    public bool IsDisabled
    {
        get
        {
            lock (_gate)
            {
                return _disabled;
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var now = _clock();
        var line = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
            .Append(" [").Append(LevelCode(level)).Append("] ")
            .Append(category).Append(": ")
            .Append(message);

        if (exception is not null)
        {
            line.AppendLine().Append(exception);
        }

        var text = Redact(line.ToString());

        lock (_gate)
        {
            if (_disabled || _disposed)
            {
                return;
            }

            try
            {
                EnsureWriter(DateOnly.FromDateTime(now.DateTime));
                _writer!.WriteLine(text);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                _disabled = true;
                CloseWriter();
            }
        }
    }

    /// <summary>Replaces the user-profile path (which contains the Windows user name) with a placeholder.</summary>
    internal string Redact(string text) =>
        string.IsNullOrEmpty(_profilePath)
            ? text
            : text.Replace(_profilePath, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);

    private void EnsureWriter(DateOnly day)
    {
        if (_writer is not null && day == _writerDay)
        {
            return;
        }

        CloseWriter();
        System.IO.Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"safeclean-{day:yyyyMMdd}.log");

        // FileShare.ReadWrite lets the user open the log in an editor while the app is running.
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        _writerDay = day;
    }

    private void CloseWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch (IOException)
        {
            // Nothing useful can be done if the final flush fails.
        }

        _writer = null;
    }

    private static string LevelCode(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            CloseWriter();
        }
    }
}
