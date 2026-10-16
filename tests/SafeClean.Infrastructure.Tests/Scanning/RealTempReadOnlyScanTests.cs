using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SafeClean.Core.Models;
using SafeClean.Infrastructure.Scanning;

namespace SafeClean.Infrastructure.Tests.Scanning;

/// <summary>
/// A read-only scan of this PC's real Temp folder. Marked Explicit, so it only runs when asked for
/// (it is slower and depends on this machine). It prints totals only — never file names.
/// </summary>
public sealed class RealTempReadOnlyScanTests(ITestOutputHelper output)
{
    [Fact(Explicit = true)]
    public async Task ScanRealTempFolder_ReadOnly_PrintTotals()
    {
        var inspector = new FileInspector();
        var scanner = new TempScanner(
            new TempRootResolver(inspector, NullLogger<TempRootResolver>.Instance),
            inspector,
            TimeProvider.System,
            NullLogger<TempScanner>.Instance);

        var stopwatch = Stopwatch.StartNew();
        var result = await scanner.ScanAsync(null, TestContext.Current.CancellationToken);
        stopwatch.Stop();

        Assert.True(result.Succeeded, "Refused: " + string.Join(", ", result.RootDecision.Problems));
        var session = result.Session;
        var summary = session.Summary;

        output.WriteLine($"Duration            : {stopwatch.Elapsed.TotalSeconds:N1} s");
        output.WriteLine($"Folders scanned     : {session.FoldersScanned:N0}");
        output.WriteLine($"Entries listed      : {summary.TotalCount:N0}");
        output.WriteLine($"Eligible            : {summary.EligibleCount:N0} files, {summary.EligibleBytes / 1048576.0:N1} MB");
        output.WriteLine($"Needs review        : {summary.NeedsReviewCount:N0} files, {summary.NeedsReviewBytes / 1048576.0:N1} MB");
        output.WriteLine($"Skipped             : {summary.SkippedCount:N0} entries, {summary.SkippedBytes / 1048576.0:N1} MB");
        output.WriteLine($"Estimated recoverable: {summary.EstimatedRecoverableBytes / 1048576.0:N1} MB (estimate)");
        output.WriteLine($"Vanished during scan: {session.VanishedDuringScan:N0}");
        output.WriteLine($"Truncated           : {session.WasTruncated}");
        output.WriteLine("Skip reasons (an entry can have several):");
        foreach (var group in session.Candidates.SelectMany(c => c.Decision.Reasons).GroupBy(r => r).OrderByDescending(g => g.Count()))
        {
            output.WriteLine($"  {group.Key,-24}: {group.Count():N0}");
        }

        Assert.All(session.Candidates, c => Assert.StartsWith(session.Root.CanonicalPath + @"\", c.Path, StringComparison.Ordinal));
        Assert.All(session.Candidates.Where(c => c.Decision.IsSelectable), c => Assert.Empty(c.Decision.Reasons));
    }
}
