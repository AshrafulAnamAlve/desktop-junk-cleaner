using System.Diagnostics.CodeAnalysis;

namespace SafeClean.Core.Models;

/// <summary>Either a completed <see cref="ScanSession"/>, or the reasons the Temp folder was refused.</summary>
public sealed class ScanResult
{
    private ScanResult(RootDecision rootDecision, ScanSession? session)
    {
        RootDecision = rootDecision;
        Session = session;
    }

    public RootDecision RootDecision { get; }

    public ScanSession? Session { get; }

    [MemberNotNullWhen(true, nameof(Session))]
    public bool Succeeded => Session is not null;

    public static ScanResult Refused(RootDecision rootDecision)
    {
        ArgumentNullException.ThrowIfNull(rootDecision);
        if (rootDecision.IsApproved)
        {
            throw new ArgumentException("An approved root is not a refusal.", nameof(rootDecision));
        }

        return new ScanResult(rootDecision, null);
    }

    public static ScanResult Completed(RootDecision rootDecision, ScanSession session)
    {
        ArgumentNullException.ThrowIfNull(rootDecision);
        ArgumentNullException.ThrowIfNull(session);
        return new ScanResult(rootDecision, session);
    }
}
