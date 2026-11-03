namespace SafeClean.Core.Interfaces;

/// <summary>Whether SafeClean can still write its log file. The user is told when it can't.</summary>
public interface ILogStatus
{
    /// <summary>True after a write failure turned file logging off for the rest of the session.</summary>
    bool IsDisabled { get; }

    /// <summary>Raised once, when logging turns off. It can come from any thread.</summary>
    event EventHandler? Disabled;
}
