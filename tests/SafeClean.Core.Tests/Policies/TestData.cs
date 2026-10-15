using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.Core.Tests.Policies;

/// <summary>Synthetic paths and facts for policy tests. Nothing here touches the disk.</summary>
internal static class TestData
{
    public const string Profile = @"C:\Users\Example";
    public const string TempRoot = @"C:\Users\Example\AppData\Local\Temp";

    public static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    public static readonly FileIdentity RootIdentity = new(0x1234, 42UL);
    public static readonly FileIdentity SomeFileIdentity = new(0x1234, 1000UL);

    public static ProtectedLocations Protected { get; } = new(
        subtrees:
        [
            @"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData",
            @"C:\$Recycle.Bin", $@"{Profile}\Documents", $@"{Profile}\Desktop", $@"{Profile}\Downloads",
        ],
        exactPaths: [@"C:\Users", Profile, $@"{Profile}\AppData", $@"{Profile}\AppData\Local", $@"{Profile}\AppData\Roaming"]);

    /// <summary>Facts for a healthy Temp folder; tests change one field at a time with <c>with</c>.</summary>
    public static RootFacts GoodRootFacts(string path = TempRoot) =>
        new(path + @"\", path, FileAttributes.Directory, AnyAncestorIsReparsePoint: false, IsFixedDrive: true, "NTFS", RootIdentity, RootIdentity);

    public static ApprovedRoot ApprovedRoot(string path = TempRoot)
    {
        var decision = RootPolicy.Evaluate(GoodRootFacts(path), Protected);
        Assert.True(decision.IsApproved, "Test root should be approved: " + string.Join(", ", decision.Problems));
        return decision.Root;
    }

    public static CandidatePolicyContext Context(
        ProtectedLocations? protectedLocations = null,
        TimeSpan? minimumAge = null,
        IEnumerable<string>? exclusions = null) =>
        new(ApprovedRoot(), protectedLocations ?? Protected, Now, minimumAge, exclusions);

    /// <summary>An ordinary 30-day-old file inside the Temp root; eligible unless a test changes something.</summary>
    public static FileFacts OldFile(string relativePath)
    {
        var path = TempRoot + @"\" + relativePath;
        return new FileFacts(path, path, FileAttributes.Archive, 4096, Now.AddDays(-30), Now.AddDays(-30), SomeFileIdentity, 1);
    }

    public static CandidateDecision Evaluate(FileFacts facts, CandidatePolicyContext? context = null) =>
        CandidatePolicy.Evaluate(facts, context ?? Context());
}
