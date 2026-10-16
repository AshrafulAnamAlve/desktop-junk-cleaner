using Microsoft.Extensions.Logging.Abstractions;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;
using SafeClean.Infrastructure.Scanning;
using SafeClean.Infrastructure.Tests.Sandbox;

namespace SafeClean.Infrastructure.Tests.Scanning;

/// <summary>
/// Root approval against the real file system. Junction cases use sandbox folders only.
/// The two tests that touch the real Temp folder only open it to read its attributes and ID.
/// </summary>
public sealed class RootResolutionTests : IDisposable
{
    private readonly TestSandbox _sandbox = TestSandbox.Create();
    private readonly FileInspector _inspector = new();

    public void Dispose() => _sandbox.Dispose();

    private static readonly string RealUserTemp = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Temp");

    [Fact]
    public void RealTempFolderOnThisPcIsApproved_ReadOnly()
    {
        var resolution = new TempRootResolver(_inspector, NullLogger<TempRootResolver>.Instance).Resolve();

        Assert.True(resolution.Decision.IsApproved, "Refused: " + string.Join(", ", resolution.Decision.Problems));
        Assert.EndsWith(@"\AppData\Local\Temp", resolution.Decision.Root.CanonicalPath, StringComparison.OrdinalIgnoreCase);
        Assert.False(resolution.Decision.Root.Identity.IsUnknown);
    }

    [Fact]
    public void TempRedirectedToAnotherFolderIsRefused()
    {
        // As if TEMP pointed at the sandbox: it is a perfectly normal folder, but not %LOCALAPPDATA%\Temp.
        var otherFolder = _sandbox.CreateDirectory("not-temp");

        var facts = RootFactsReader.Read(otherFolder, RealUserTemp, _inspector);
        var decision = RootPolicy.Evaluate(facts, KnownProtectedLocations.Create(NullLogger.Instance));

        Assert.False(decision.IsApproved);
        Assert.Equal([RootProblem.NotUserTempDirectory], decision.Problems);
    }

    [Fact]
    public void SandboxFolderPassesTheSameRules()
    {
        var root = _sandbox.CreateDirectory("root");

        var resolution = new SandboxRootResolver(root).Resolve();

        Assert.True(resolution.Decision.IsApproved, "Refused: " + string.Join(", ", resolution.Decision.Problems));
        Assert.Equal(root, resolution.Decision.Root.CanonicalPath);
    }

    [Fact]
    public void RootThatIsAJunctionIsRefused()
    {
        var real = _sandbox.CreateDirectory("real-root");
        var link = _sandbox.PathFor("link-root");
        FileSystemLinks.CreateJunction(link, real);

        var decision = new SandboxRootResolver(link).Resolve().Decision;

        Assert.False(decision.IsApproved);
        Assert.Contains(RootProblem.RootIsReparsePoint, decision.Problems);
    }

    [Fact]
    public void RootBelowAJunctionIsRefused()
    {
        var realParent = _sandbox.CreateDirectory("real-parent");
        Directory.CreateDirectory(Path.Combine(realParent, "child"));
        var linkParent = _sandbox.PathFor("link-parent");
        FileSystemLinks.CreateJunction(linkParent, realParent);

        var decision = new SandboxRootResolver(Path.Combine(linkParent, "child")).Resolve().Decision;

        Assert.False(decision.IsApproved);
        Assert.Contains(RootProblem.AncestorIsReparsePoint, decision.Problems);
    }

    [Fact]
    public void MissingRootIsRefused()
    {
        var decision = new SandboxRootResolver(_sandbox.PathFor("missing")).Resolve().Decision;

        Assert.False(decision.IsApproved);
        Assert.Contains(RootProblem.NotUserTempDirectory, decision.Problems); // identity could not be read
    }

    [Fact]
    public void ProtectedLocationsOnThisPcIncludeTheImportantFolders()
    {
        var locations = KnownProtectedLocations.Create(NullLogger.Instance);
        string Folder(System.Environment.SpecialFolder f) => System.Environment.GetFolderPath(f);

        Assert.True(locations.Protects(Path.Combine(Folder(System.Environment.SpecialFolder.Windows), "System32", "kernel32.dll")));
        Assert.True(locations.Protects(Path.Combine(Folder(System.Environment.SpecialFolder.ProgramFiles), "App", "app.exe")));
        Assert.True(locations.Protects(Path.Combine(Folder(System.Environment.SpecialFolder.MyDocuments), "file.docx")));
        Assert.True(locations.Protects(Path.Combine(Folder(System.Environment.SpecialFolder.DesktopDirectory), "file.txt")));
        Assert.True(locations.Protects(Path.Combine(Folder(System.Environment.SpecialFolder.UserProfile), "Downloads", "setup.exe")));
        Assert.True(locations.Protects(Folder(System.Environment.SpecialFolder.UserProfile)));
        Assert.False(locations.Protects(Path.Combine(RealUserTemp, "something.tmp")));
        Assert.Null(locations.FindRootConflict(RealUserTemp));
    }
}
