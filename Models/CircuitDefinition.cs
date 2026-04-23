using System.Windows;
using System.Windows.Media;

namespace F1TrackMapper.Models;

public sealed class CircuitDefinition
{
    private const double CanvasPadding = 28;

    private readonly IReadOnlyList<Point> _trackPoints;
    private readonly IReadOnlyList<Point>? _pitLanePoints;
    private readonly double[] _segmentLengths;
    private readonly double _totalLength;
    private readonly Rect _bounds;
    private readonly double _rawWidth;
    private readonly double _rawHeight;

    public CircuitDefinition(
        string key,
        string displayName,
        string location,
        string summary,
        string accentHex,
        double referenceLapSeconds,
        double averageSpeedKph,
        IReadOnlyList<Point> trackPoints,
        IReadOnlyList<TrackMarker> markers,
        IReadOnlyList<Point>? pitLanePoints = null)
    {
        if (trackPoints.Count < 3)
        {
            throw new ArgumentException("A circuit requires at least three points.", nameof(trackPoints));
        }

        Key = key;
        DisplayName = displayName;
        Location = location;
        Summary = summary;
        AccentHex = accentHex;
        ReferenceLapSeconds = referenceLapSeconds;
        AverageSpeedKph = averageSpeedKph;
        Markers = markers;
        _trackPoints = EnsureClosed(trackPoints);
        _pitLanePoints = pitLanePoints?.Count >= 2 ? pitLanePoints.ToList() : null;

        var minX = _trackPoints.Min(point => point.X);
        var maxX = _trackPoints.Max(point => point.X);
        var minY = _trackPoints.Min(point => point.Y);
        var maxY = _trackPoints.Max(point => point.Y);

        if (_pitLanePoints is not null && _pitLanePoints.Count > 0)
        {
            minX = Math.Min(minX, _pitLanePoints.Min(point => point.X));
            maxX = Math.Max(maxX, _pitLanePoints.Max(point => point.X));
            minY = Math.Min(minY, _pitLanePoints.Min(point => point.Y));
            maxY = Math.Max(maxY, _pitLanePoints.Max(point => point.Y));
        }

        if (markers.Count > 0)
        {
            minX = Math.Min(minX, markers.Min(marker => marker.TrackPoint.X));
            maxX = Math.Max(maxX, markers.Max(marker => marker.TrackPoint.X));
            minY = Math.Min(minY, markers.Min(marker => marker.TrackPoint.Y));
            maxY = Math.Max(maxY, markers.Max(marker => marker.TrackPoint.Y));
        }

        _bounds = new Rect(new Point(minX, minY), new Point(maxX, maxY));
        _rawWidth = Math.Max(1, _bounds.Width);
        _rawHeight = Math.Max(1, _bounds.Height);

        _segmentLengths = new double[_trackPoints.Count - 1];
        for (var index = 0; index < _trackPoints.Count - 1; index++)
        {
            var dx = _trackPoints[index + 1].X - _trackPoints[index].X;
            var dy = _trackPoints[index + 1].Y - _trackPoints[index].Y;
            _segmentLengths[index] = Math.Sqrt((dx * dx) + (dy * dy));
            _totalLength += _segmentLengths[index];
        }
    }

    public string Key { get; }
    public string DisplayName { get; }
    public string Location { get; }
    public string Summary { get; }
    public string AccentHex { get; }
    public double ReferenceLapSeconds { get; }
    public double AverageSpeedKph { get; }
    public IReadOnlyList<TrackMarker> Markers { get; }
    public IReadOnlyList<Point> RawTrackPoints => _trackPoints;

    public Geometry CreateTrackGeometry(double width, double height)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();

        context.BeginFigure(MapToCanvas(_trackPoints[0], width, height), false, true);
        context.PolyLineTo(_trackPoints.Skip(1).Select(point => MapToCanvas(point, width, height)).ToList(), true, true);

        geometry.Freeze();
        return geometry;
    }

    public Geometry? CreatePitLaneGeometry(double width, double height)
    {
        if (_pitLanePoints is null || _pitLanePoints.Count < 2)
        {
            return null;
        }

        var explicitPitGeometry = new StreamGeometry();
        using var explicitPitContext = explicitPitGeometry.Open();
        explicitPitContext.BeginFigure(MapToCanvas(_pitLanePoints[0], width, height), false, false);
        explicitPitContext.PolyLineTo(_pitLanePoints.Skip(1).Select(point => MapToCanvas(point, width, height)).ToList(), true, true);
        explicitPitGeometry.Freeze();
        return explicitPitGeometry;
    }

    public Point GetCanvasPoint(double progress, double width, double height, double lateralOffset = 0)
    {
        var normalizedProgress = progress % 1.0;
        if (normalizedProgress < 0)
        {
            normalizedProgress += 1.0;
        }

        var targetLength = normalizedProgress * _totalLength;
        var traversed = 0.0;
        var scale = GetScale(width, height);

        for (var index = 0; index < _segmentLengths.Length; index++)
        {
            var nextTraversed = traversed + _segmentLengths[index];
            if (targetLength <= nextTraversed)
            {
                var segmentProgress = _segmentLengths[index] == 0
                    ? 0
                    : (targetLength - traversed) / _segmentLengths[index];

                var current = _trackPoints[index];
                var next = _trackPoints[index + 1];
                var point = new Point(
                    current.X + ((next.X - current.X) * segmentProgress),
                    current.Y + ((next.Y - current.Y) * segmentProgress));

                var tangent = new Vector((next.X - current.X) * scale, (current.Y - next.Y) * scale);
                if (tangent.Length > 0.001)
                {
                    tangent.Normalize();
                }

                var normal = new Vector(-tangent.Y, tangent.X);
                var canvasPoint = MapToCanvas(point, width, height);
                return new Point(
                    canvasPoint.X + (normal.X * lateralOffset),
                    canvasPoint.Y + (normal.Y * lateralOffset));
            }

            traversed = nextTraversed;
        }

        return MapToCanvas(_trackPoints[0], width, height);
    }

    public bool TryProjectRawPointToProgress(Point rawPoint, out double progress, out double distance)
    {
        var bestDistanceSquared = double.MaxValue;
        var bestProgress = 0.0;
        var traversed = 0.0;

        for (var index = 0; index < _segmentLengths.Length; index++)
        {
            var start = _trackPoints[index];
            var end = _trackPoints[index + 1];
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var segmentLengthSquared = (dx * dx) + (dy * dy);

            double t;
            Point projection;
            if (segmentLengthSquared <= 0.000001)
            {
                t = 0;
                projection = start;
            }
            else
            {
                t = (((rawPoint.X - start.X) * dx) + ((rawPoint.Y - start.Y) * dy)) / segmentLengthSquared;
                t = Math.Max(0, Math.Min(1, t));
                projection = new Point(start.X + (dx * t), start.Y + (dy * t));
            }

            var distX = rawPoint.X - projection.X;
            var distY = rawPoint.Y - projection.Y;
            var distanceSquared = (distX * distX) + (distY * distY);
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                var segmentProgressDistance = _segmentLengths[index] * t;
                bestProgress = _totalLength <= 0.001 ? 0 : (traversed + segmentProgressDistance) / _totalLength;
            }

            traversed += _segmentLengths[index];
        }

        progress = bestProgress;
        distance = Math.Sqrt(bestDistanceSquared);
        return bestDistanceSquared < double.MaxValue;
    }

    public Point MapMarkerPoint(Point rawPoint, double width, double height)
        => MapToCanvas(rawPoint, width, height);

    private double GetScale(double width, double height)
    {
        var drawableWidth = Math.Max(1, width - (CanvasPadding * 2));
        var drawableHeight = Math.Max(1, height - (CanvasPadding * 2));
        return Math.Min(drawableWidth / _rawWidth, drawableHeight / _rawHeight);
    }

    private Point MapToCanvas(Point rawPoint, double width, double height)
    {
        var drawableWidth = Math.Max(1, width - (CanvasPadding * 2));
        var drawableHeight = Math.Max(1, height - (CanvasPadding * 2));
        var scale = Math.Min(drawableWidth / _rawWidth, drawableHeight / _rawHeight);
        var centeredOffsetX = CanvasPadding + ((drawableWidth - (_rawWidth * scale)) / 2.0);
        var centeredOffsetY = CanvasPadding + ((drawableHeight - (_rawHeight * scale)) / 2.0);

        var x = centeredOffsetX + ((rawPoint.X - _bounds.Left) * scale);
        var y = centeredOffsetY + ((_bounds.Bottom - rawPoint.Y) * scale);
        return new Point(x, y);
    }

    private static IReadOnlyList<Point> EnsureClosed(IReadOnlyList<Point> points)
    {
        if (points[0] == points[^1])
        {
            return points;
        }

        var closed = points.ToList();
        closed.Add(points[0]);
        return closed;
    }
}
