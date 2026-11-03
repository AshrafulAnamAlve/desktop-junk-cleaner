using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SafeClean.App.ViewModels;
using SafeClean.Core.Models;
using SafeClean.Infrastructure.Logging;

namespace SafeClean.App.Tests;

/// <summary>
/// Phase 11: checks the <b>compiled</b> SafeClean programs, not their source text. <c>ForbiddenApiTests</c> reads the
/// source and could be fooled by unusual spelling or generated code; these read what the compiler actually produced.
/// </summary>
public sealed class CompiledAssemblyTests
{
    private static readonly Assembly Core = typeof(CleanupMode).Assembly;
    private static readonly Assembly Infrastructure = typeof(FileLoggerProvider).Assembly;
    private static readonly Assembly App = typeof(MainViewModel).Assembly;

    /// <summary>Every call SafeClean makes straight into Windows. A new one must be reviewed and added here on purpose.</summary>
    private static readonly string[] AllowedNativeCalls =
    [
        "dwmapi.dll!DwmSetWindowAttribute",            // dark title bar
        "kernel32.dll!CreateFileW",                    // open a handle (scanner: read attributes only; deleter: exclusive)
        "kernel32.dll!GetFileInformationByHandleEx",   // read a file's facts
        "kernel32.dll!GetFinalPathNameByHandleW",      // the path Windows reports
        "kernel32.dll!GetSystemTimes",                 // processor reading
        "kernel32.dll!GetVolumeInformationByHandleW",  // is this drive NTFS?
        "kernel32.dll!GlobalMemoryStatusEx",           // memory reading
        "kernel32.dll!SetFileInformationByHandle",     // the one deletion call
        "shell32.dll!SHGetKnownFolderPath",            // find folders to protect
    ];

    /// <summary>Libraries that have no place in SafeClean: network, registry, services, permissions, system management.</summary>
    private static readonly string[] ForbiddenLibraries =
    [
        "System.Net",
        "Microsoft.Win32.Registry",
        "System.ServiceProcess",
        "System.Management",
        "System.DirectoryServices",
        "System.IO.FileSystem.AccessControl",
        "System.Security.AccessControl",
        "System.Security.Cryptography.ProtectedData",
    ];

    /// <summary>Framework calls that delete, overwrite, change files or control other programs.</summary>
    private static readonly string[] ForbiddenCalls =
    [
        "System.IO.File::Delete",
        "System.IO.File::Move",
        "System.IO.File::Copy",
        "System.IO.File::Replace",
        "System.IO.File::SetAttributes",
        "System.IO.File::Encrypt",
        "System.IO.File::Decrypt",
        "System.IO.Directory::Delete",
        "System.IO.Directory::Move",
        "System.IO.FileInfo::Delete",
        "System.IO.FileInfo::MoveTo",
        "System.IO.FileInfo::CopyTo",
        "System.IO.FileInfo::Replace",
        "System.IO.FileInfo::Encrypt",
        "System.IO.FileInfo::Decrypt",
        "System.IO.DirectoryInfo::Delete",
        "System.IO.DirectoryInfo::MoveTo",
        "System.IO.FileSystemInfo::Delete",
        "System.IO.FileSystemInfo::set_Attributes",
        "System.Diagnostics.Process::Start",
        "System.Diagnostics.Process::Kill",
        "System.Diagnostics.Process::CloseMainWindow",
        "System.Diagnostics.Process::set_PriorityClass",
        "System.Diagnostics.Process::set_PriorityBoostEnabled",
        "System.Diagnostics.Process::set_ProcessorAffinity",
        "System.Diagnostics.Process::set_MaxWorkingSet",
        "System.Diagnostics.Process::set_MinWorkingSet",
        "System.Diagnostics.ProcessStartInfo::.ctor",
        "System.Environment::SetEnvironmentVariable",
    ];

    /// <summary>The settings store replaces its own settings file atomically and keeps a copy of an unusable one (docs/SECURITY.md §13).</summary>
    private static readonly string[] AllowedInInfrastructure = ["System.IO.File::Move", "System.IO.File::Copy"];

    private static IEnumerable<string> NativeCalls(Assembly assembly)
    {
        using var stream = File.OpenRead(assembly.Location);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var calls = new List<string>();
        foreach (var handle in reader.MethodDefinitions)
        {
            var import = reader.GetMethodDefinition(handle).GetImport();
            if (!import.Module.IsNil)
            {
                calls.Add(reader.GetString(reader.GetModuleReference(import.Module).Name).ToLowerInvariant() + "!" + reader.GetString(import.Name));
            }
        }

        return calls;
    }

    private static HashSet<string> FrameworkCalls(Assembly assembly)
    {
        using var stream = File.OpenRead(assembly.Location);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var calls = new HashSet<string>();
        foreach (var handle in reader.MemberReferences)
        {
            var member = reader.GetMemberReference(handle);
            if (member.Parent.Kind == HandleKind.TypeReference)
            {
                var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                calls.Add($"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}::{reader.GetString(member.Name)}");
            }
        }

        return calls;
    }

    [Fact]
    public void NativeCalls_AreExactlyTheReviewedOnes()
    {
        var found = new[] { Core, Infrastructure, App }.SelectMany(NativeCalls).Distinct().Order(StringComparer.Ordinal);

        Assert.Equal(AllowedNativeCalls.Order(StringComparer.Ordinal), found);
    }

    [Fact]
    public void Core_MakesNoNativeCallsAtAll() => Assert.Empty(NativeCalls(Core));

    [Fact]
    public void TheOneDeletionCall_LivesOnlyInInfrastructure() =>
        Assert.DoesNotContain("kernel32.dll!SetFileInformationByHandle", NativeCalls(App));

    [Theory]
    [InlineData("Core")]
    [InlineData("Infrastructure")]
    [InlineData("App")]
    public void NoForbiddenLibraryIsReferenced(string which)
    {
        var assembly = which switch { "Core" => Core, "Infrastructure" => Infrastructure, _ => App };

        var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.NotEmpty(referenced);
        Assert.All(referenced, name => Assert.DoesNotContain(ForbiddenLibraries, forbidden => name.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData("Core")]
    [InlineData("App")]
    public void NoForbiddenCallIsCompiledIn(string which)
    {
        var calls = FrameworkCalls(which == "Core" ? Core : App);

        Assert.All(ForbiddenCalls, forbidden => Assert.DoesNotContain(forbidden, calls));
    }

    [Fact]
    public void Infrastructure_UsesOnlyTheTwoAllowedFileCalls_AndReallyUsesThem()
    {
        var calls = FrameworkCalls(Infrastructure);

        Assert.Equal(AllowedInInfrastructure.Order(StringComparer.Ordinal), ForbiddenCalls.Where(calls.Contains).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheReaderReallySeesFrameworkCalls()
    {
        // Guards against the tests above passing only because nothing was read.
        Assert.Contains("System.IO.File::Exists", FrameworkCalls(Infrastructure));
        Assert.NotEmpty(NativeCalls(Infrastructure));
    }
}
