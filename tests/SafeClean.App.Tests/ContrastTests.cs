using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace SafeClean.App.Tests;

/// <summary>
/// Colour contrast of the theme, computed from the real values in Theme.xaml using the WCAG 2 formula.
/// Text needs at least 4.5:1; large text and control parts (bars, borders, focus rings) at least 3:1.
/// </summary>
public sealed class ContrastTests(ITestOutputHelper output)
{
    private static readonly Lazy<Dictionary<string, string>> Colors = new(LoadThemeColors);

    public static TheoryData<string, string, double> Pairs() => new()
    {
        // Body and caption text on every surface it appears on
        { "Text.Primary", "Window", 4.5 }, { "Text.Primary", "Card", 4.5 }, { "Text.Primary", "Sidebar", 4.5 },
        { "Text.Primary", "Hover", 4.5 }, { "Text.Primary", "Selected", 4.5 },
        { "Text.Secondary", "Window", 4.5 }, { "Text.Secondary", "Card", 4.5 }, { "Text.Secondary", "Sidebar", 4.5 },
        { "Text.Muted", "Window", 4.5 }, { "Text.Muted", "Card", 4.5 }, { "Text.Muted", "Sidebar", 4.5 }, { "Text.Muted", "Hover", 4.5 },

        // Button text in every state
        { "OnAccent", "Accent", 4.5 }, { "OnAccent", "Accent.Hover", 4.5 }, { "OnAccent", "Accent.Pressed", 4.5 },
        { "OnDanger", "Danger", 4.5 }, { "OnDanger", "Danger.Hover", 4.5 }, { "OnDanger", "Danger.Pressed", 4.5 },

        // Badges, notices and status text
        { "Accent.SoftText", "Accent.Soft", 4.5 },
        { "Danger", "Danger.Soft", 4.5 }, { "Danger", "Card", 4.5 }, { "Danger", "Window", 4.5 },
        { "Warning", "Warning.Soft", 4.5 }, { "Warning", "Card", 4.5 }, { "Warning", "Window", 4.5 },
        { "Success", "Success.Soft", 4.5 }, { "Success", "Card", 4.5 },

        // Non-text parts: progress bars, chart lines, focus ring, red borders and switch
        { "Accent", "Card", 3 }, { "Accent", "Window", 3 }, { "Success", "Window", 3 },
        { "Focus", "Window", 3 }, { "Focus", "Card", 3 }, { "Focus", "Sidebar", 3 },
        { "Danger", "Sidebar", 3 },
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void ThemeColours_MeetTheContrastMinimum(string foreground, string background, double minimum)
    {
        var ratio = Ratio(Colors.Value["Color." + foreground], Colors.Value["Color." + background]);
        output.WriteLine($"{foreground} on {background}: {ratio:F2}:1 (minimum {minimum}:1)");

        Assert.True(ratio >= minimum, $"{foreground} on {background} is only {ratio:F2}:1 (needs {minimum}:1).");
    }

    [Fact]
    public void DisabledText_IsRecorded()
    {
        // WCAG exempts disabled controls; recorded so the figure is known.
        var ratio = Ratio(Colors.Value["Color.Disabled.Text"], Colors.Value["Color.Disabled.Background"]);
        output.WriteLine($"Disabled.Text on Disabled.Background: {ratio:F2}:1 (exempt)");
        Assert.True(ratio > 1);
    }

    internal static double Ratio(string foregroundHex, string backgroundHex)
    {
        var a = Luminance(foregroundHex);
        var b = Luminance(backgroundHex);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string hex)
    {
        var rgb = hex.TrimStart('#');
        if (rgb.Length == 8)
        {
            rgb = rgb[2..]; // ignore alpha (all tested colours are opaque)
        }

        double Channel(int offset)
        {
            var c = int.Parse(rgb.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(0)) + (0.7152 * Channel(2)) + (0.0722 * Channel(4));
    }

    private static Dictionary<string, string> LoadThemeColors()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SafeClean.sln")))
        {
            directory = directory.Parent;
        }

        var theme = Path.Combine(directory!.FullName, "src", "SafeClean.App", "Resources", "Theme.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(theme).Descendants()
            .Where(e => e.Name.LocalName == "Color" && e.Attribute(x + "Key") is not null)
            .ToDictionary(e => e.Attribute(x + "Key")!.Value, e => e.Value.Trim());
    }
}
