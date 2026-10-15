using System.Diagnostics.CodeAnalysis;

namespace SafeClean.Core.Models;

/// <summary>Either an <see cref="ApprovedRoot"/> or the list of problems that prevented approval.</summary>
public sealed class RootDecision
{
    private RootDecision(ApprovedRoot? root, IReadOnlyList<RootProblem> problems)
    {
        Root = root;
        Problems = problems;
    }

    public ApprovedRoot? Root { get; }

    public IReadOnlyList<RootProblem> Problems { get; }

    [MemberNotNullWhen(true, nameof(Root))]
    public bool IsApproved => Root is not null;

    internal static RootDecision Approved(ApprovedRoot root) => new(root, []);

    internal static RootDecision Refused(IReadOnlyList<RootProblem> problems) => new(null, problems);
}
