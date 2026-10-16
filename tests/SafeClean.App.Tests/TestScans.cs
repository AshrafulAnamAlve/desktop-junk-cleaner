using System.IO;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.App.Tests;

/// <summary>Builds synthetic scan results through the real Core policies. No disk access.</summary>
internal static class TestScans
{
    public const string Root = @"C:\Users\Example\AppData\Local\Temp";

    public static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly FileIdentity RootIdentity = new(1, 1UL);
    private static readonly ProtectedLocations Protected = new([@"C:\Users\Example\Documents"], [@"C:\Users\Example"]);

    private static readonly RootFacts GoodRoot = new(Root, Root, FileAttributes.Directory, false, true, "NTFS", RootIdentity, RootIdentity);

    /// <summary>a.tmp, b.tmp, setup.log, c.dat (safe) · installer.exe (review) · fresh.tmp (skipped).</summary>
    public static ScanResult Standard() => Completed(
        ("a.tmp", 100, 30),
        ("b.tmp", 200, 30),
        (@"logs\setup.log", 50, 30),
        ("c.dat", 30, 30),
        ("installer.exe", 1000, 30),
        ("fresh.tmp", 70, 0));

    public static ScanResult Completed(params (string RelativePath, long Size, int AgeDays)[] files)
    {
        var decision = RootPolicy.Evaluate(GoodRoot, Protected);
        var context = new CandidatePolicyContext(decision.Root!, Protected, Now);
        var candidates = files.Select((file, i) =>
        {
            var path = Root + @"\" + file.RelativePath;
            var time = Now.AddDays(-file.AgeDays);
            var facts = new FileFacts(path, path, FileAttributes.Archive, file.Size, time, time, new FileIdentity(1, (ulong)(i + 100)), 1);
            return new ScanCandidate(new CandidateId(i + 1), path, facts, CandidatePolicy.Evaluate(facts, context));
        }).ToList();

        var session = new ScanSession(Guid.NewGuid(), decision.Root!, Now, Now.AddSeconds(2.5), candidates, 2, 0, false);
        return ScanResult.Completed(decision, session);
    }

    public static ScanResult Refused() =>
        ScanResult.Refused(RootPolicy.Evaluate(GoodRoot with { IsFixedDrive = false, FileSystemName = "FAT32" }, Protected));
}

/// <summary>A scanner whose behaviour each test decides.</summary>
internal sealed class FakeTempScanner(Func<IProgress<ScanProgress>?, CancellationToken, Task<ScanResult>> handler) : ITempScanner
{
    public int Calls { get; private set; }

    public static FakeTempScanner Returning(ScanResult result) => new((_, _) => Task.FromResult(result));

    public Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        Calls++;
        return handler(progress, cancellationToken);
    }
}
