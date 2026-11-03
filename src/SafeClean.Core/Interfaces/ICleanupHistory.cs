using SafeClean.Core.Models;

namespace SafeClean.Core.Interfaces;

/// <summary>Append-only record of cleanups (counts and sizes only, no file names).</summary>
public interface ICleanupHistory
{
    /// <summary>Newest first. Unreadable lines are skipped and counted, never fatal.</summary>
    HistoryLoadResult Load();

    /// <summary>Adds one entry. Failing to write history never affects a cleanup.</summary>
    void Append(CleanupHistoryEntry entry);

    /// <summary>Empties the history file (it is emptied, not deleted).</summary>
    void Clear();
}
