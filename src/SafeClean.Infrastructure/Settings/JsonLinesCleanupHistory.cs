using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;

namespace SafeClean.Infrastructure.Settings;

/// <summary>
/// <c>%LOCALAPPDATA%\SafeClean\history.jsonl</c>: one JSON line per cleanup, appended. Counts and sizes only —
/// the entry type has no field for file names. Unreadable lines are skipped and counted. "Clear" empties the
/// file; it never deletes anything. Nothing is written while running as administrator (docs/SECURITY.md R8).
/// </summary>
public sealed class JsonLinesCleanupHistory(string path, ILogger<JsonLinesCleanupHistory> logger, bool writingAllowed = true) : ICleanupHistory
{
    private const int MaxEntries = 1000;
    private const long MaxFileBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Lock _gate = new();

    /// <summary>%LOCALAPPDATA%\SafeClean\history.jsonl.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "SafeClean", "history.jsonl");

    public HistoryLoadResult Load()
    {
        lock (_gate)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return new HistoryLoadResult([], 0);
                }

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (stream.Length > MaxFileBytes)
                {
                    stream.Seek(-MaxFileBytes, SeekOrigin.End); // only the newest part of a huge file
                }

                using var reader = new StreamReader(stream, Encoding.UTF8);
                var entries = new List<CleanupHistoryEntry>();
                var skipped = 0;
                while (reader.ReadLine() is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (TryParse(line) is { } entry)
                    {
                        entries.Add(entry);
                    }
                    else
                    {
                        skipped++;
                    }
                }

                return new HistoryLoadResult(entries.OrderByDescending(e => e.StartedAt).Take(MaxEntries).ToList(), skipped);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Cleanup history could not be read.");
                return new HistoryLoadResult([], 0);
            }
        }
    }

    public void Append(CleanupHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!writingAllowed)
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.WriteLine(JsonSerializer.Serialize(entry, Json));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // History is a convenience; failing to write it must never affect a cleanup.
                logger.LogWarning(ex, "Cleanup history could not be written.");
            }
        }
    }

    public void Clear()
    {
        if (!writingAllowed)
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                if (File.Exists(path))
                {
                    // Emptied, not deleted.
                    using var stream = new FileStream(path, FileMode.Truncate, FileAccess.Write, FileShare.Read);
                }

                logger.LogInformation("Cleanup history cleared.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Cleanup history could not be cleared.");
            }
        }
    }

    private static CleanupHistoryEntry? TryParse(string line)
    {
        try
        {
            var entry = JsonSerializer.Deserialize<CleanupHistoryEntry>(line, Json);
            return entry is not null
                && entry.Deleted >= 0 && entry.Skipped >= 0 && entry.Failed >= 0 && entry.BytesFreed >= 0
                && !string.IsNullOrWhiteSpace(entry.What)
                && (entry.Refusal is null || Enum.IsDefined(entry.Refusal.Value))
                ? entry
                : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}
