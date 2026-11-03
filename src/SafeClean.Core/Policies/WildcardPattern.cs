namespace SafeClean.Core.Policies;

/// <summary>
/// Simple file-name patterns for exclusions: <c>*</c> matches any run of characters, <c>?</c> exactly one.
/// Case-insensitive. Deliberately no regular expressions (nothing surprising can be written).
/// </summary>
public static class WildcardPattern
{
    public static bool IsMatch(string name, string pattern)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(pattern);

        int n = 0, p = 0, starAt = -1, matchAfterStar = 0;
        while (n < name.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || CharEquals(pattern[p], name[n])))
            {
                n++;
                p++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                starAt = p++;
                matchAfterStar = n;
            }
            else if (starAt >= 0)
            {
                p = starAt + 1;
                n = ++matchAfterStar;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    private static bool CharEquals(char a, char b) =>
        a == b || char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
}
