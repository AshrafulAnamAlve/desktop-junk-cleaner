using Microsoft.Extensions.Logging;
using SafeClean.Infrastructure.Logging;
using SafeClean.Infrastructure.Tests.Sandbox;

namespace SafeClean.Infrastructure.Tests.Logging;

public sealed class FileLoggerTests : IDisposable
{
    private const string FakeProfile = @"C:\Users\ExampleUser";
    private static readonly DateTimeOffset Day1 = new(2026, 10, 15, 9, 30, 0, TimeSpan.Zero);

    private readonly TestSandbox _sandbox = TestSandbox.Create();

    public void Dispose() => _sandbox.Dispose();

    [Fact]
    public void Log_WritesLevelCategoryAndMessage()
    {
        var directory = _sandbox.PathFor("logs");
        using (var provider = new FileLoggerProvider(directory, () => Day1, FakeProfile))
        {
            provider.CreateLogger("Test.Category").LogInformation("Hello {Name}", "world");
        }

        var text = File.ReadAllText(Path.Combine(directory, "safeclean-20261015.log"));
        Assert.Contains("[INF] Test.Category: Hello world", text);
        Assert.StartsWith("2026-10-15 09:30:00.000 +00:00", text);
    }

    [Fact]
    public void Log_RedactsUserProfilePath_CaseInsensitively()
    {
        var directory = _sandbox.PathFor("logs");
        using (var provider = new FileLoggerProvider(directory, () => Day1, FakeProfile))
        {
            var logger = provider.CreateLogger("Test");
            logger.LogWarning("Skipped {Path}", @"C:\Users\ExampleUser\AppData\Local\Temp\a.tmp");
            logger.LogWarning("Skipped {Path}", @"c:\users\exampleuser\AppData\Local\Temp\b.tmp");
        }

        var text = File.ReadAllText(Path.Combine(directory, "safeclean-20261015.log"));
        Assert.Contains(@"%USERPROFILE%\AppData\Local\Temp\a.tmp", text);
        Assert.Contains(@"%USERPROFILE%\AppData\Local\Temp\b.tmp", text);
        Assert.DoesNotContain("ExampleUser", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Log_IncludesExceptionDetails()
    {
        var directory = _sandbox.PathFor("logs");
        using (var provider = new FileLoggerProvider(directory, () => Day1, FakeProfile))
        {
            provider.CreateLogger("Test").LogError(new InvalidOperationException("boom"), "Something failed");
        }

        var text = File.ReadAllText(Path.Combine(directory, "safeclean-20261015.log"));
        Assert.Contains("[ERR] Test: Something failed", text);
        Assert.Contains("System.InvalidOperationException: boom", text);
    }

    [Fact]
    public void NewDay_StartsNewFile()
    {
        var directory = _sandbox.PathFor("logs");
        var now = Day1;
        using (var provider = new FileLoggerProvider(directory, () => now, FakeProfile))
        {
            var logger = provider.CreateLogger("Test");
            logger.LogInformation("first day");
            now = Day1.AddDays(1);
            logger.LogInformation("second day");
        }

        Assert.Contains("first day", File.ReadAllText(Path.Combine(directory, "safeclean-20261015.log")));
        Assert.Contains("second day", File.ReadAllText(Path.Combine(directory, "safeclean-20261016.log")));
    }

    [Fact]
    public void UnusableLogFolder_DisablesLoggingInsteadOfThrowing()
    {
        // A file sits where the log folder's parent should be, so the folder cannot be created.
        var blocker = _sandbox.PathFor("not-a-folder");
        File.WriteAllText(blocker, "blocks directory creation");

        using var provider = new FileLoggerProvider(Path.Combine(blocker, "logs"), () => Day1, FakeProfile);
        var exception = Record.Exception(() => provider.CreateLogger("Test").LogError("must not crash"));

        Assert.Null(exception);
        Assert.True(provider.IsDisabled);
    }

    [Fact]
    public void LoggingAfterDispose_IsIgnored()
    {
        var directory = _sandbox.PathFor("logs");
        var provider = new FileLoggerProvider(directory, () => Day1, FakeProfile);
        var logger = provider.CreateLogger("Test");
        provider.Dispose();

        var exception = Record.Exception(() => logger.LogInformation("after dispose"));

        Assert.Null(exception);
        Assert.False(File.Exists(Path.Combine(directory, "safeclean-20261015.log")));
    }

    [Fact]
    public void DefaultLogFolder_IsUnderLocalAppData_NotTemp()
    {
        var localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(localAppData, FileLoggerProvider.DefaultDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.False(
            FileLoggerProvider.DefaultDirectory.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase),
            "Logs must never live inside the Temp folder that SafeClean cleans.");
    }
}
