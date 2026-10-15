namespace SafeClean.Core.Models;

/// <summary>What SafeClean learned about one file by asking Windows through a read-only handle.</summary>
/// <param name="Path">Where the file was found while walking the approved root.</param>
/// <param name="FinalPath">
/// Where Windows says the opened file really is. If anything redirected the lookup (for example a folder
/// that was swapped for a junction), this differs from <paramref name="Path"/>.
/// </param>
/// <param name="Attributes">The file's attributes, including any bits .NET has no name for.</param>
/// <param name="Size">Logical size in bytes.</param>
/// <param name="CreationTimeUtc">When the file was created.</param>
/// <param name="LastWriteTimeUtc">When the file's contents were last changed.</param>
/// <param name="Identity">Volume serial number and file ID.</param>
/// <param name="LinkCount">How many names (hard links) the file has on disk.</param>
public sealed record FileFacts(
    string Path,
    string FinalPath,
    FileAttributes Attributes,
    long Size,
    DateTimeOffset CreationTimeUtc,
    DateTimeOffset LastWriteTimeUtc,
    FileIdentity Identity,
    uint LinkCount);
