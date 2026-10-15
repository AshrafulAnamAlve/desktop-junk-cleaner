namespace SafeClean.Core.Models;

/// <summary>
/// A folder that passed every root rule (R1–R7 in docs/SECURITY.md). Only
/// <see cref="Policies.RootPolicy"/> can create one, so holding an instance proves the checks were made.
/// </summary>
public sealed class ApprovedRoot
{
    internal ApprovedRoot(string canonicalPath, FileIdentity identity)
    {
        CanonicalPath = canonicalPath;
        Identity = identity;
    }

    /// <summary>The path Windows reported for the opened folder, without a trailing separator.</summary>
    public string CanonicalPath { get; }

    public FileIdentity Identity { get; }

    public override string ToString() => CanonicalPath;
}
