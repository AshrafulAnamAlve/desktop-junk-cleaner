using SafeClean.Core.Models;

namespace SafeClean.Core.Policies;

/// <summary>
/// Decides whether the Temp folder may be used as the cleanup root (rules R1–R7 in docs/SECURITY.md).
/// This is the only code that can create an <see cref="ApprovedRoot"/>.
/// </summary>
public static class RootPolicy
{
    public static RootDecision Evaluate(RootFacts facts, ProtectedLocations protectedLocations)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(protectedLocations);

        var problems = new List<RootProblem>();
        var requested = WindowsPath.TrimTrailingSeparator(facts.RequestedPath ?? string.Empty);
        var final = WindowsPath.TrimTrailingSeparator(facts.FinalPath ?? string.Empty);

        // R2: plain local drive paths only.
        var pathsValid = PathValidator.IsValid(requested) && PathValidator.IsValid(final);
        if (!pathsValid)
        {
            problems.Add(RootProblem.InvalidPath);
        }

        // R3: internal NTFS drive (file IDs and hard-link counts are reliable there).
        if (!facts.IsFixedDrive)
        {
            problems.Add(RootProblem.NotFixedDrive);
        }

        if (!string.Equals(facts.FileSystemName, "NTFS", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(RootProblem.NotNtfs);
        }

        // R4: a real folder, with no redirection on it or above it.
        if (!facts.Attributes.HasFlag(FileAttributes.Directory))
        {
            problems.Add(RootProblem.NotADirectory);
        }

        if (facts.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            problems.Add(RootProblem.RootIsReparsePoint);
        }

        if (facts.AnyAncestorIsReparsePoint)
        {
            problems.Add(RootProblem.AncestorIsReparsePoint);
        }

        // R5: exactly the user's own %LOCALAPPDATA%\Temp folder (same identity), never a redirected TEMP.
        if (!facts.Identity.Matches(facts.ExpectedIdentity))
        {
            problems.Add(RootProblem.NotUserTempDirectory);
        }

        // R6: not a drive root, not protected, not inside or around a protected location.
        if (pathsValid)
        {
            foreach (var path in new[] { requested, final })
            {
                if (protectedLocations.FindRootConflict(path) is { } conflict && !problems.Contains(conflict))
                {
                    problems.Add(conflict);
                }
            }
        }

        // R7: everything later is compared against the path Windows reported, not one we built.
        return problems.Count == 0
            ? RootDecision.Approved(new ApprovedRoot(final, facts.Identity))
            : RootDecision.Refused(problems);
    }
}
