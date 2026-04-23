using System.Windows;
using F1TrackMapper.Models;

namespace F1TrackMapper.Services;

public static class ActualCircuitBuilder
{
    private const double ReferenceCanvasWidth = 960;
    private const double ReferenceCanvasHeight = 620;
    private const double CanvasPadding = 28;

    private static readonly string[] AdaptiveRotationCircuits =
    {
        "Saudi Arabian Grand Prix",
        "Canadian Grand Prix",
        "Monaco Grand Prix",
        "British Grand Prix",
        "Belgian Grand Prix",
        "Hungarian Grand Prix",
        "Qatar Grand Prix",
        "Abu Dhabi Grand Prix"
    };

    public static CircuitDefinition Build(CalendarEntry weekend, MultiViewerCircuitInfoDto? circuitInfo, GeoJsonCircuitFeatureDto? geoCircuit)
    {
        var useMultiViewerLayout = circuitInfo is not null && circuitInfo.X.Count >= 3 && circuitInfo.Y.Count >= 3;
        var useGeoJsonLayout = !useMultiViewerLayout && geoCircuit is not null && geoCircuit.Geometry.ToPoints().Count >= 3;

        var trackPoints = useMultiViewerLayout
            ? BuildMultiViewerPoints(circuitInfo)
            : useGeoJsonLayout
                ? geoCircuit!.Geometry.ToPoints().ToList()
                : new List<Point>();

        var title = !string.IsNullOrWhiteSpace(circuitInfo?.CircuitName)
            ? circuitInfo!.CircuitName
            : !string.IsNullOrWhiteSpace(geoCircuit?.Properties.Name)
                ? geoCircuit!.Properties.Name
                : weekend.CircuitShortName;

        var markers = new List<TrackMarker>();
        IReadOnlyList<Point>? pitLanePoints = null;

        if (useMultiViewerLayout && circuitInfo is not null)
        {
            markers.AddRange(circuitInfo.Corners.Select(corner => new TrackMarker(
                $"T{corner.Number}",
                $"Turn {corner.Number}",
                corner.TrackPosition.ToPoint(),
                corner.Angle,
                MarkerKind.Turn)));

            markers.AddRange(circuitInfo.MarshalSectors.Select(sector => new TrackMarker(
                $"M{sector.Number}",
                string.Empty,
                sector.TrackPosition.ToPoint(),
                sector.Angle,
                MarkerKind.Sector)));

            pitLanePoints = BuildPitLanePoints(trackPoints, circuitInfo.Corners, weekend, title);
        }

        if (pitLanePoints is not null && pitLanePoints.Count >= 2)
        {
            var pitMarkerPoint = pitLanePoints[Math.Min(1, pitLanePoints.Count - 1)];
            markers.Add(new TrackMarker(
                "PIT",
                "Pit entry / lane",
                pitMarkerPoint,
                0,
                MarkerKind.Pit));
        }

        (trackPoints, pitLanePoints, markers) = NormalizeOrientation(weekend, title, trackPoints, pitLanePoints, markers);

        var lapTime = circuitInfo?.CandidateLap?.LapTime ?? 90.0;
        var summary = useMultiViewerLayout
            ? "Track layout comes from the live circuit metadata feed for the selected weekend."
            : "Track layout comes from the bacinger/f1-circuits GeoJSON fallback dataset.";

        return new CircuitDefinition(
            weekend.CircuitKey.ToString(),
            title,
            $"{weekend.Location}, {weekend.CountryName}",
            summary,
            useGeoJsonLayout ? "#FF5449" : "#E10600",
            lapTime,
            220.0,
            trackPoints,
            markers,
            pitLanePoints);
    }

    private static (List<Point> TrackPoints, IReadOnlyList<Point>? PitLanePoints, List<TrackMarker> Markers) NormalizeOrientation(
        CalendarEntry weekend,
        string title,
        List<Point> trackPoints,
        IReadOnlyList<Point>? pitLanePoints,
        List<TrackMarker> markers)
    {
        if (trackPoints.Count < 3)
        {
            return (trackPoints, pitLanePoints, markers);
        }

        var rotation = GetRotationOverride(weekend, title, trackPoints);
        if (Math.Abs(rotation) < 0.01)
        {
            return (trackPoints, pitLanePoints, markers);
        }

        var bounds = GetBounds(trackPoints, pitLanePoints, markers);
        var center = new Point(bounds.Left + (bounds.Width / 2.0), bounds.Top + (bounds.Height / 2.0));

        var rotatedTrack = trackPoints.Select(point => Rotate(point, center, rotation)).ToList();
        var rotatedPit = pitLanePoints?.Select(point => Rotate(point, center, rotation)).ToList();
        var rotatedMarkers = markers
            .Select(marker => marker with
            {
                TrackPoint = Rotate(marker.TrackPoint, center, rotation),
                Angle = marker.Angle - rotation
            })
            .ToList();

        return (rotatedTrack, rotatedPit, rotatedMarkers);
    }

    private static IReadOnlyList<Point>? BuildPitLanePoints(IReadOnlyList<Point> trackPoints, IReadOnlyList<MultiViewerMarkerDto> corners, CalendarEntry weekend, string title)
    {
        if (trackPoints.Count < 6 || corners.Count < 2)
        {
            return null;
        }

        var orderedCorners = corners
            .Where(corner => corner.Number > 0)
            .OrderBy(corner => corner.Number)
            .ToList();

        if (orderedCorners.Count < 2)
        {
            return null;
        }

        if (Matches(weekend.GrandPrixName, "Canadian") || Matches(title, "Montreal") || Matches(weekend.Location, "Montreal"))
        {
            var canadianPit = BuildCanadianPitLanePoints(trackPoints, orderedCorners);
            if (canadianPit is not null)
            {
                return canadianPit;
            }
        }

        var firstCorner = orderedCorners.First();
        var lastCorner = orderedCorners.Last();
        var firstCornerIndex = FindClosestTrackIndex(trackPoints, firstCorner.TrackPosition.ToPoint());
        var lastCornerIndex = FindClosestTrackIndex(trackPoints, lastCorner.TrackPosition.ToPoint());
        var startFinishPath = GetShorterPath(trackPoints, lastCornerIndex, firstCornerIndex);
        var trimmedPath = TrimPath(startFinishPath);

        if (trimmedPath.Count < 2)
        {
            return null;
        }

        var scale = GetReferenceScale(trackPoints);
        if (scale <= 0.001)
        {
            return null;
        }

        var rawOffset = 24.0 / scale;
        var center = GetTrackCenter(trackPoints);
        var sideSign = DeterminePitSide(trimmedPath, center, rawOffset);
        return OffsetPath(trimmedPath, rawOffset, sideSign);
    }

    private static IReadOnlyList<Point>? BuildCanadianPitLanePoints(IReadOnlyList<Point> trackPoints, IReadOnlyList<MultiViewerMarkerDto> orderedCorners)
    {
        var corner13 = orderedCorners.FirstOrDefault(corner => corner.Number == 13);
        var corner14 = orderedCorners.FirstOrDefault(corner => corner.Number == 14);
        var corner1 = orderedCorners.FirstOrDefault(corner => corner.Number == 1);
        var corner2 = orderedCorners.FirstOrDefault(corner => corner.Number == 2);

        if (corner13 is null || corner14 is null || corner1 is null || corner2 is null)
        {
            return null;
        }

        var index13 = FindClosestTrackIndex(trackPoints, corner13.TrackPosition.ToPoint());
        var index14 = FindClosestTrackIndex(trackPoints, corner14.TrackPosition.ToPoint());
        var index1 = FindClosestTrackIndex(trackPoints, corner1.TrackPosition.ToPoint());
        var index2 = FindClosestTrackIndex(trackPoints, corner2.TrackPosition.ToPoint());

        var path13To14 = GetShorterPath(trackPoints, index13, index14).ToList();
        var path14To1 = GetShorterPath(trackPoints, index14, index1).ToList();
        var path1To2 = GetShorterPath(trackPoints, index1, index2).ToList();
        var combinedPath = ConcatenatePaths(path13To14, path14To1, path1To2);
        var trimmedPath = TrimPathWithRatios(combinedPath, 0.10, 0.22);
        if (trimmedPath.Count < 2)
        {
            return null;
        }

        var scale = GetReferenceScale(trackPoints);
        if (scale <= 0.001)
        {
            return null;
        }

        var rawOffset = 18.0 / scale;
        var center = GetTrackCenter(trackPoints);
        var sideSign = DeterminePitSide(trimmedPath, center, rawOffset);
        return OffsetPath(trimmedPath, rawOffset, sideSign);
    }

    private static IReadOnlyList<Point> ConcatenatePaths(params List<Point>[] segments)
    {
        var combined = new List<Point>();
        foreach (var segment in segments)
        {
            if (segment.Count == 0)
            {
                continue;
            }

            if (combined.Count == 0)
            {
                combined.AddRange(segment);
            }
            else
            {
                combined.AddRange(segment.Skip(1));
            }
        }

        return combined;
    }

    private static IReadOnlyList<Point> TrimPathWithRatios(IReadOnlyList<Point> path, double startRatio, double endRatio)
    {
        if (path.Count < 5)
        {
            return path.ToList();
        }

        var skipStart = Math.Max(1, (int)Math.Round(path.Count * startRatio));
        var skipEnd = Math.Max(1, (int)Math.Round(path.Count * endRatio));
        var takeCount = path.Count - skipStart - skipEnd;
        if (takeCount < 2)
        {
            return path.ToList();
        }

        return path.Skip(skipStart).Take(takeCount).ToList();
    }

    private static int DeterminePitSide(IReadOnlyList<Point> path, Point center, double rawOffset)
    {
        var midIndex = path.Count / 2;
        var tangent = GetPathTangent(path, midIndex);
        if (tangent.Length <= 0.001)
        {
            return 1;
        }

        tangent.Normalize();
        var normal = new Vector(-tangent.Y, tangent.X);
        var candidateA = path[midIndex] + (normal * rawOffset);
        var candidateB = path[midIndex] - (normal * rawOffset);

        return DistanceSquared(candidateA, center) <= DistanceSquared(candidateB, center) ? 1 : -1;
    }

    private static IReadOnlyList<Point> OffsetPath(IReadOnlyList<Point> path, double rawOffset, int sideSign)
    {
        var points = new List<Point>(path.Count);
        for (var index = 0; index < path.Count; index++)
        {
            var tangent = GetPathTangent(path, index);
            if (tangent.Length <= 0.001)
            {
                points.Add(path[index]);
                continue;
            }

            tangent.Normalize();
            var normal = new Vector(-tangent.Y, tangent.X) * sideSign;
            points.Add(path[index] + (normal * rawOffset));
        }

        return points;
    }

    private static Vector GetPathTangent(IReadOnlyList<Point> path, int index)
    {
        var previous = index == 0 ? path[index] : path[index - 1];
        var next = index == path.Count - 1 ? path[index] : path[index + 1];
        return new Vector(next.X - previous.X, next.Y - previous.Y);
    }

    private static IReadOnlyList<Point> TrimPath(IReadOnlyList<Point> path)
    {
        if (path.Count < 5)
        {
            return path.ToList();
        }

        var skipStart = Math.Max(1, (int)Math.Round(path.Count * 0.12));
        var skipEnd = Math.Max(1, (int)Math.Round(path.Count * 0.15));
        var takeCount = path.Count - skipStart - skipEnd;

        if (takeCount < 2)
        {
            return path.ToList();
        }

        return path.Skip(skipStart).Take(takeCount).ToList();
    }

    private static IReadOnlyList<Point> GetShorterPath(IReadOnlyList<Point> trackPoints, int startIndex, int endIndex)
    {
        var forward = TraversePath(trackPoints, startIndex, endIndex, 1);
        var backward = TraversePath(trackPoints, startIndex, endIndex, -1);
        return GetPathLength(forward) <= GetPathLength(backward) ? forward : backward;
    }

    private static List<Point> TraversePath(IReadOnlyList<Point> trackPoints, int startIndex, int endIndex, int direction)
    {
        var count = trackPoints.Count;
        var path = new List<Point>();
        var index = startIndex;

        while (true)
        {
            path.Add(trackPoints[index]);
            if (index == endIndex)
            {
                break;
            }

            index = (index + direction + count) % count;
        }

        return path;
    }

    private static double GetPathLength(IReadOnlyList<Point> points)
    {
        var length = 0.0;
        for (var index = 0; index < points.Count - 1; index++)
        {
            var dx = points[index + 1].X - points[index].X;
            var dy = points[index + 1].Y - points[index].Y;
            length += Math.Sqrt((dx * dx) + (dy * dy));
        }

        return length;
    }

    private static int FindClosestTrackIndex(IReadOnlyList<Point> trackPoints, Point target)
    {
        var closestIndex = 0;
        var closestDistance = double.MaxValue;

        for (var index = 0; index < trackPoints.Count; index++)
        {
            var distance = DistanceSquared(trackPoints[index], target);
            if (distance >= closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            closestIndex = index;
        }

        return closestIndex;
    }

    private static double GetReferenceScale(IReadOnlyList<Point> trackPoints)
    {
        var bounds = GetBounds(trackPoints, null, Array.Empty<TrackMarker>());
        var rawWidth = Math.Max(1, bounds.Width);
        var rawHeight = Math.Max(1, bounds.Height);
        var drawableWidth = Math.Max(1, ReferenceCanvasWidth - (CanvasPadding * 2));
        var drawableHeight = Math.Max(1, ReferenceCanvasHeight - (CanvasPadding * 2));
        return Math.Min(drawableWidth / rawWidth, drawableHeight / rawHeight);
    }

    private static double GetRotationOverride(CalendarEntry weekend, string title, IReadOnlyList<Point> trackPoints)
    {
        if (Matches(weekend.GrandPrixName, "Las Vegas") || Matches(title, "Las Vegas") || Matches(weekend.Location, "Las Vegas"))
        {
            return 90;
        }

        if (!AdaptiveRotationCircuits.Any(name => Matches(weekend.GrandPrixName, name) || Matches(title, name)))
        {
            return 0;
        }

        var currentScore = GetFitScore(trackPoints);
        var rotatedScore = GetFitScore(trackPoints.Select(point => Rotate(point, GetTrackCenter(trackPoints), 90)).ToList());
        return rotatedScore > currentScore * 1.07 ? 90 : 0;
    }

    private static double GetFitScore(IReadOnlyList<Point> points)
    {
        var bounds = GetBounds(points, null, Array.Empty<TrackMarker>());
        var width = Math.Max(1, bounds.Width);
        var height = Math.Max(1, bounds.Height);
        var scale = Math.Min((ReferenceCanvasWidth - 56.0) / width, (ReferenceCanvasHeight - 56.0) / height);
        return scale * width * height;
    }

    private static Point GetTrackCenter(IReadOnlyList<Point> points)
    {
        var bounds = GetBounds(points, null, Array.Empty<TrackMarker>());
        return new Point(bounds.Left + (bounds.Width / 2.0), bounds.Top + (bounds.Height / 2.0));
    }

    private static Rect GetBounds(IReadOnlyList<Point> points, IReadOnlyList<Point>? pitLanePoints, IReadOnlyList<TrackMarker> markers)
    {
        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);

        if (pitLanePoints is not null && pitLanePoints.Count > 0)
        {
            minX = Math.Min(minX, pitLanePoints.Min(point => point.X));
            maxX = Math.Max(maxX, pitLanePoints.Max(point => point.X));
            minY = Math.Min(minY, pitLanePoints.Min(point => point.Y));
            maxY = Math.Max(maxY, pitLanePoints.Max(point => point.Y));
        }

        if (markers.Count > 0)
        {
            minX = Math.Min(minX, markers.Min(marker => marker.TrackPoint.X));
            maxX = Math.Max(maxX, markers.Max(marker => marker.TrackPoint.X));
            minY = Math.Min(minY, markers.Min(marker => marker.TrackPoint.Y));
            maxY = Math.Max(maxY, markers.Max(marker => marker.TrackPoint.Y));
        }

        return new Rect(new Point(minX, minY), new Point(maxX, maxY));
    }

    private static Point Rotate(Point point, Point center, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        var translatedX = point.X - center.X;
        var translatedY = point.Y - center.Y;

        var rotatedX = (translatedX * Math.Cos(radians)) - (translatedY * Math.Sin(radians));
        var rotatedY = (translatedX * Math.Sin(radians)) + (translatedY * Math.Cos(radians));

        return new Point(center.X + rotatedX, center.Y + rotatedY);
    }

    private static double DistanceSquared(Point left, Point right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return (dx * dx) + (dy * dy);
    }

    private static bool Matches(string left, string right)
        => left.Contains(right, StringComparison.OrdinalIgnoreCase) || right.Contains(left, StringComparison.OrdinalIgnoreCase);

    private static List<Point> BuildMultiViewerPoints(MultiViewerCircuitInfoDto? circuitInfo)
    {
        if (circuitInfo is null)
        {
            return new List<Point>();
        }

        var points = new List<Point>(Math.Min(circuitInfo.X.Count, circuitInfo.Y.Count));
        for (var index = 0; index < Math.Min(circuitInfo.X.Count, circuitInfo.Y.Count); index++)
        {
            points.Add(new Point(circuitInfo.X[index], circuitInfo.Y[index]));
        }

        return points;
    }
}

