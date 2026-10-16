using System.Globalization;
using System.Text;

namespace SafeClean.App.Presentation;

/// <summary>Formatting for values shown to the user.</summary>
public static class DisplayText
{
    private static readonly string[] Units = ["KB", "MB", "GB", "TB"];

    /// <summary>
    /// Makes invisible and text-direction characters visible, e.g. <c>"‮gpj.exe"</c> becomes
    /// <c>"⟦U+202E⟧gpj.exe"</c>, so a file name can't disguise its real extension or hide characters.
    /// Zero-width joiners are kept because emoji sequences need them.
    /// </summary>
    public static string Reveal(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        StringBuilder? builder = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (IsDisguising(c))
            {
                builder ??= new StringBuilder(text, 0, i, text.Length + 16);
                builder.Append(CultureInfo.InvariantCulture, $"⟦U+{(int)c:X4}⟧");
            }
            else
            {
                builder?.Append(c);
            }
        }

        return builder?.ToString() ?? text;
    }

    /// <summary>File sizes the way Windows Explorer shows them: 512 bytes, 1.5 KB, 3.4 GB.</summary>
    public static string FormatBytes(long bytes, IFormatProvider? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (bytes <= 0)
        {
            return "0 bytes";
        }

        if (bytes == 1)
        {
            return "1 byte";
        }

        if (bytes < 1024)
        {
            return string.Format(culture, "{0:N0} bytes", bytes);
        }

        double value = bytes;
        var unit = -1;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Format(culture, value >= 100 ? "{0:N0} {1}" : "{0:N1} {1}", value, Units[unit]);
    }

    /// <summary>
    /// A date in the user's local time and format. The extreme values the scanner uses for impossible
    /// Windows timestamps (and anything before 1601, when Windows time starts) are shown as "Invalid date".
    /// </summary>
    public static string FormatDate(DateTimeOffset value, IFormatProvider? culture = null)
    {
        if (value == DateTimeOffset.MinValue || value == DateTimeOffset.MaxValue || value.UtcDateTime.Year < 1601)
        {
            return "Invalid date";
        }

        try
        {
            return value.ToLocalTime().ToString("g", culture ?? CultureInfo.CurrentCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "Invalid date";
        }
    }

    /// <summary>"1 file" / "3 files".</summary>
    public static string Count(int count, string singular, string plural, IFormatProvider? culture = null) =>
        string.Format(culture ?? CultureInfo.CurrentCulture, "{0:N0} {1}", count, count == 1 ? singular : plural);

    private static bool IsDisguising(char c) =>
        c < ' ' || c == '\u007F'
        || c is '؜' or '​' or '‎' or '‏' or '﻿'
        || c is >= '‪' and <= '‮'
        || c is >= '⁦' and <= '⁩';
}
