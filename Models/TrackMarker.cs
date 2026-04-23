using System.Windows;

namespace F1TrackMapper.Models;

public enum MarkerKind
{
    Turn,
    Sector,
    Pit
}

public sealed record TrackMarker(
    string Title,
    string Subtitle,
    Point TrackPoint,
    double Angle,
    MarkerKind Kind);
