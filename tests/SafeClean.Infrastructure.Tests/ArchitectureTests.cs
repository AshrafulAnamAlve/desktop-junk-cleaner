using SafeClean.Infrastructure.Logging;

namespace SafeClean.Infrastructure.Tests;

/// <summary>Infrastructure talks to Windows, never to the user interface.</summary>
public sealed class ArchitectureTests
{
    [Fact]
    public void Infrastructure_DoesNotReferenceUiAssemblies()
    {
        var referenced = typeof(FileLoggerProvider).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain("PresentationFramework", referenced);
        Assert.DoesNotContain("PresentationCore", referenced);
        Assert.DoesNotContain("System.Windows.Forms", referenced);
        Assert.DoesNotContain("SafeClean", referenced); // the App assembly
    }
}
