using System.Globalization;
using SafeClean.App.Mvvm;
using SafeClean.App.Presentation;

namespace SafeClean.App.Tests;

public sealed class PresentationTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData(-5, "0 bytes")]
    [InlineData(0, "0 bytes")]
    [InlineData(1, "1 byte")]
    [InlineData(1023, "1,023 bytes")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(150 * 1024, "150 KB")]
    [InlineData(3_640_000_000, "3.4 GB")]
    [InlineData(5_000_000_000_000, "4.5 TB")]
    public void FormatBytes(long bytes, string expected)
    {
        Assert.Equal(expected, DisplayText.FormatBytes(bytes, English));
    }

    [Theory]
    [InlineData("‮gpj.exe", "⟦U+202E⟧gpj.exe")]
    [InlineData("a‏b", "a⟦U+200F⟧b")]
    [InlineData("tab\there", "tab⟦U+0009⟧here")]
    [InlineData("zero​width", "zero⟦U+200B⟧width")]
    public void RevealMakesDisguisingCharactersVisible(string input, string expected)
    {
        Assert.Equal(expected, DisplayText.Reveal(input));
    }

    [Theory]
    [InlineData("normal-file.tmp")]
    [InlineData("report 😀.tmp")]
    [InlineData("تقرير.tmp")]
    [InlineData("family 👨‍👩‍👧.tmp")] // zero-width joiners inside an emoji are kept
    public void RevealLeavesOrdinaryNamesAlone(string name)
    {
        Assert.Same(name, DisplayText.Reveal(name));
    }

    [Fact]
    public void ImpossibleDatesAreLabelled()
    {
        Assert.Equal("Invalid date", DisplayText.FormatDate(DateTimeOffset.MaxValue, English));
        Assert.Equal("Invalid date", DisplayText.FormatDate(DateTimeOffset.MinValue, English));
        Assert.NotEqual("Invalid date", DisplayText.FormatDate(new DateTimeOffset(2026, 10, 1, 8, 30, 0, TimeSpan.Zero), English));
    }

    [Fact]
    public void CountUsesSingularAndPlural()
    {
        Assert.Equal("1 file", DisplayText.Count(1, "file", "files", English));
        Assert.Equal("2,500 files", DisplayText.Count(2500, "file", "files", English));
    }

    [Fact]
    public void RelayCommand_DoesNothingWhenDisabled()
    {
        var runs = 0;
        var enabled = false;
        var command = new RelayCommand(() => runs++, () => enabled);

        command.Execute(null);
        enabled = true;
        command.Execute(null);

        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task AsyncRelayCommand_IsDisabledWhileRunning()
    {
        var release = new TaskCompletionSource();
        var runs = 0;
        var command = new AsyncRelayCommand(async () =>
        {
            runs++;
            await release.Task;
        });

        var first = command.ExecuteAsync();
        Assert.True(command.IsRunning);
        Assert.False(command.CanExecute(null));
        await command.ExecuteAsync();
        release.SetResult();
        await first;

        Assert.Equal(1, runs);
        Assert.True(command.CanExecute(null));
    }
}
