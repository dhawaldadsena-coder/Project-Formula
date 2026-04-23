namespace F1TrackMapper.Models;

public sealed record DriverDefinition(
    string Code,
    string FullName,
    string TeamName,
    string TeamBadge,
    string TeamLogoPath,
    string AccentHex,
    double PaceBias,
    int? DriverNumber = null);

public sealed record DriverSnapshot(
    DriverDefinition Driver,
    int Position,
    int CompletedLaps,
    double Progress,
    double SpeedKph,
    double GapSeconds,
    string GapLabel,
    double LaneOffset);
