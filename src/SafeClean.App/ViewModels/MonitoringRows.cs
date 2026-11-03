using SafeClean.App.Presentation;
using SafeClean.Core.Models;

namespace SafeClean.App.ViewModels;

/// <summary>One drive on the Performance page.</summary>
public sealed class DriveRowViewModel(DriveSnapshot drive)
{
    public string Title { get; } = string.IsNullOrWhiteSpace(drive.Label)
        ? $"Drive ({drive.Name.TrimEnd('\\')})"
        : $"{drive.Label} ({drive.Name.TrimEnd('\\')})";

    public string UsedText { get; } = DisplayText.FormatPercent(drive.UsedPercent) + " used";

    public string DetailText { get; } = $"{DisplayText.FormatBytes(drive.FreeBytes)} free of {DisplayText.FormatBytes(drive.TotalBytes)} · {drive.Format}";

    public double UsedPercent { get; } = drive.UsedPercent ?? 0;
}

/// <summary>One program in the read-only memory table. Values Windows won't give show as "n/a".</summary>
public sealed class ProcessRowViewModel(ProcessMemoryInfo process)
{
    private const string NotAvailable = "n/a";

    public ProcessMemoryInfo Process { get; } = process;

    public string Name { get; } = process.Name is null ? NotAvailable : DisplayText.Reveal(process.Name);

    public int Id { get; } = process.Id;

    public string WorkingSetText { get; } = process.WorkingSetBytes is { } bytes ? DisplayText.FormatBytes(bytes) : NotAvailable;

    public string PrivateText { get; } = process.PrivateBytes is { } bytes ? DisplayText.FormatBytes(bytes) : NotAvailable;
}

/// <summary>Columns the program table can be sorted by.</summary>
public enum ProcessSortColumn
{
    WorkingSet,
    PrivateBytes,
    Name,
    Id,
}
