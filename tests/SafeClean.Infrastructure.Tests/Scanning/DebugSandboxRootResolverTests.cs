#if DEBUG
using Microsoft.Extensions.Logging.Abstractions;
using SafeClean.Core.Models;
using SafeClean.Infrastructure.Cleanup;
using SafeClean.Infrastructure.Scanning;
using SafeClean.Infrastructure.Tests.Cleanup;
using SafeClean.Infrastructure.Tests.Sandbox;

namespace SafeClean.Infrastructure.Tests.Scanning;

/// <summary>The Debug-only sandbox mode: a marker file is required, and there is never a fallback to the real Temp folder.</summary>
public sealed class DebugSandboxRootResolverTests : IDisposable
{
    private readonly TestSandbox _sandbox = TestSandbox.Create();
    private readonly string _root;

    public DebugSandboxRootResolverTests() => _root = _sandbox.CreateDirectory("manual");

    public void Dispose() => _sandbox.Dispose();

    private DebugSandboxRootResolver Resolver(string? root = null) =>
        new(root ?? _root, new FileInspector(), NullLogger<DebugSandboxRootResolver>.Instance);

    private void AddMarker(string? folder = null) =>
        File.WriteAllText(Path.Combine(folder ?? _root, DebugSandboxRootResolver.MarkerFileName), "test sandbox");

    [Fact]
    public void FolderWithMarker_IsApproved_AsItself()
    {
        AddMarker();

        var resolution = Resolver().Resolve();

        Assert.True(resolution.Decision.IsApproved, string.Join(", ", resolution.Decision.Problems));
        Assert.Equal(_root, resolution.Decision.Root.CanonicalPath);
    }

    [Fact]
    public void FolderWithoutMarker_IsRefused_NeverReplacedByTheRealTempFolder()
    {
        var resolution = Resolver().Resolve();

        Assert.False(resolution.Decision.IsApproved);
        Assert.Contains(RootProblem.NotUserTempDirectory, resolution.Decision.Problems);
    }

    [Fact]
    public void MarkerThatIsAFolder_DoesNotCount()
    {
        Directory.CreateDirectory(Path.Combine(_root, DebugSandboxRootResolver.MarkerFileName));

        Assert.False(Resolver().HasMarker());
        Assert.False(Resolver().Resolve().Decision.IsApproved);
    }

    [Fact]
    public void SandboxThatIsAJunction_IsRefused_EvenWithAMarkerInItsTarget()
    {
        var target = _sandbox.CreateDirectory("elsewhere");
        AddMarker(target);
        var link = _sandbox.PathFor("link");
        FileSystemLinks.CreateJunction(link, target);

        var resolution = Resolver(link).Resolve();

        Assert.False(resolution.Decision.IsApproved);
        Assert.Contains(RootProblem.RootIsReparsePoint, resolution.Decision.Problems);
    }

    [Fact]
    public async Task MarkerRemovedAfterTheScan_RefusesTheCleanup_AndNothingIsDeleted()
    {
        AddMarker();
        var file = _sandbox.CreateFile(@"manual\old.tmp", "synthetic");
        var resolver = Resolver();
        var scan = await new TempScanner(resolver, new FileInspector(), TimeProvider.System, NullLogger<TempScanner>.Instance)
            .ScanAsync(null, TestContext.Current.CancellationToken);
        Assert.True(scan.Succeeded);
        File.Delete(Path.Combine(_root, DebugSandboxRootResolver.MarkerFileName));

        var report = await new CleanupService(resolver, new FakeElevationProbe(false), new FileInspector(), TimeProvider.System, NullLogger<CleanupService>.Instance)
            .CleanAsync(scan.Session, [Assert.Single(scan.Session.Candidates, c => c.Path == file).Id], CleanupMode.Real, null, TestContext.Current.CancellationToken);

        Assert.Equal(CleanupRefusal.RootRefused, report.Refusal);
        Assert.Equal("synthetic", File.ReadAllText(file));
    }
}
#endif
