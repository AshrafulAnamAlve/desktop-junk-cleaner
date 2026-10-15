namespace SafeClean.Core.Models;

/// <summary>Why <see cref="Policies.PathValidator"/> rejected a path.</summary>
public enum PathProblem
{
    Empty,
    TooLong,
    SegmentTooLong,
    DevicePath,
    NetworkPath,
    NotFullyQualified,
    ForwardSlash,
    EmptySegment,
    DotSegment,
    AlternateDataStream,
    InvalidCharacter,
    ControlCharacter,
    TrailingDotOrSpace,
    ReservedDeviceName,
}
