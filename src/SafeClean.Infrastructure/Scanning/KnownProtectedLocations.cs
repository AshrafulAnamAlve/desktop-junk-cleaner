using Microsoft.Extensions.Logging;
using SafeClean.Core.Policies;
using SafeClean.Infrastructure.Native;

namespace SafeClean.Infrastructure.Scanning;

/// <summary>Builds the protected-location list (docs/SECURITY.md §3) from this PC's real folder locations.</summary>
public static class KnownProtectedLocations
{
    private static readonly System.Environment.SpecialFolder[] SubtreeFolders =
    [
        System.Environment.SpecialFolder.Windows,
        System.Environment.SpecialFolder.System,
        System.Environment.SpecialFolder.SystemX86,
        System.Environment.SpecialFolder.ProgramFiles,
        System.Environment.SpecialFolder.ProgramFilesX86,
        System.Environment.SpecialFolder.CommonProgramFiles,
        System.Environment.SpecialFolder.CommonProgramFilesX86,
        System.Environment.SpecialFolder.CommonApplicationData,
        System.Environment.SpecialFolder.Fonts,
        System.Environment.SpecialFolder.DesktopDirectory,
        System.Environment.SpecialFolder.MyDocuments,
        System.Environment.SpecialFolder.MyMusic,
        System.Environment.SpecialFolder.MyPictures,
        System.Environment.SpecialFolder.MyVideos,
        System.Environment.SpecialFolder.Favorites,
        System.Environment.SpecialFolder.Recent,
        System.Environment.SpecialFolder.StartMenu,
        System.Environment.SpecialFolder.Startup,
        System.Environment.SpecialFolder.Templates,
        System.Environment.SpecialFolder.CommonDesktopDirectory,
        System.Environment.SpecialFolder.CommonDocuments,
        System.Environment.SpecialFolder.CommonMusic,
        System.Environment.SpecialFolder.CommonPictures,
        System.Environment.SpecialFolder.CommonVideos,
        System.Environment.SpecialFolder.CommonStartMenu,
        System.Environment.SpecialFolder.CommonStartup,
        System.Environment.SpecialFolder.CommonTemplates,
    ];

    // Known folders that have no SpecialFolder value: Downloads, Saved Games, Contacts, Links, Searches, 3D Objects.
    private static readonly Guid[] KnownFolderIds =
    [
        new("374DE290-123F-4565-9164-39C4925E467B"),
        new("4C5C32FF-BB9D-43B0-B5B4-2D72E54EAAA4"),
        new("56784854-C6CB-462B-8169-88E350ACB882"),
        new("BFB9D5E0-C6A9-404C-B2B2-AE6DB6AF4968"),
        new("7D1D3A04-DEBB-4115-95CF-2F29DA2920DA"),
        new("31C0DD25-9439-4F12-BF41-7FF4EDA38722"),
    ];

    private static readonly string[] OneDriveVariables = ["OneDrive", "OneDriveConsumer", "OneDriveCommercial"];

    public static ProtectedLocations Create(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var subtrees = new List<string?>();
        subtrees.AddRange(SubtreeFolders.Select(folder => System.Environment.GetFolderPath(folder, System.Environment.SpecialFolderOption.DoNotVerify)));
        subtrees.AddRange(KnownFolderIds.Select(HandleQueries.TryGetKnownFolderPath));
        subtrees.AddRange(OneDriveVariables.Select(System.Environment.GetEnvironmentVariable));

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            subtrees.Add(drive.RootDirectory.FullName + "$Recycle.Bin");
            subtrees.Add(drive.RootDirectory.FullName + "System Volume Information");
        }

        var profile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile, System.Environment.SpecialFolderOption.DoNotVerify);
        var exactPaths = new List<string?>
        {
            Path.GetDirectoryName(profile),
            profile,
            Path.Combine(profile, "AppData"),
            Path.Combine(profile, "AppData", "LocalLow"),
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData, System.Environment.SpecialFolderOption.DoNotVerify),
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData, System.Environment.SpecialFolderOption.DoNotVerify),
        };

        return new ProtectedLocations(KeepLocalPaths(subtrees, logger), KeepLocalPaths(exactPaths, logger));
    }

    /// <summary>
    /// Folders redirected to a network share can't be inside the local Temp folder, and the path validator
    /// rejects them, so they are left out (and counted in the log) instead of crashing the app.
    /// </summary>
    private static List<string> KeepLocalPaths(IEnumerable<string?> paths, ILogger logger)
    {
        var kept = new List<string>();
        var dropped = 0;
        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var path = WindowsPath.TrimTrailingSeparator(raw);
            if (PathValidator.IsValid(path))
            {
                kept.Add(path);
            }
            else
            {
                dropped++;
            }
        }

        if (dropped > 0)
        {
            logger.LogInformation("{Count} protected folder location(s) are not local drive paths and were left out.", dropped);
        }

        return kept;
    }
}
