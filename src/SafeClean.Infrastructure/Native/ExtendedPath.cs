namespace SafeClean.Infrastructure.Native;

/// <summary>
/// The <c>\\?\</c> prefix tells Windows to use a path exactly as given (no clean-up of "." or "/") and
/// allows paths longer than 260 characters even when the Windows long-path setting is off.
/// It is only ever added to paths that already passed <see cref="Core.Policies.PathValidator"/>.
/// </summary>
internal static class ExtendedPath
{
    private const string Prefix = @"\\?\";
    private const string UncPrefix = @"\\?\UNC\";

    public static string Add(string validatedPath) => Prefix + validatedPath;

    /// <summary>
    /// Turns <c>\\?\C:\x</c> back into <c>C:\x</c>. Network results (<c>\\?\UNC\...</c>) and anything else
    /// unusual are returned unchanged, so the path validator rejects them.
    /// </summary>
    public static string Remove(string osPath) =>
        osPath.StartsWith(Prefix, StringComparison.Ordinal) && !osPath.StartsWith(UncPrefix, StringComparison.OrdinalIgnoreCase)
            ? osPath[Prefix.Length..]
            : osPath;
}
