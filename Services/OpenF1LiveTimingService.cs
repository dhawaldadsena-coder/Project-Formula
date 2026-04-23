using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using F1TrackMapper.Models;

namespace F1TrackMapper.Services;

public sealed class OpenF1LiveTimingService : IDisposable
{
    private readonly OpenF1ApiClient _apiClient = new();
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<int, DriverDefinition> _driversByNumber = new();
    private readonly Dictionary<int, DriverSnapshot> _lastSnapshotsByNumber = new();

    private CircuitDefinition? _circuit;
    private CalendarEntry? _weekend;
    private OpenF1SessionDto? _session;
    private TrackTransform? _currentTransform;
    private bool _pollInProgress;

    public OpenF1LiveTimingService()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _timer.Tick += OnTick;
    }

    public event EventHandler<IReadOnlyList<DriverSnapshot>>? TelemetryUpdated;

    public string LastStatusMessage { get; private set; } = "Live timing is idle.";

    public async Task<bool> TryActivateAsync(CalendarEntry weekend, CircuitDefinition circuit, CancellationToken cancellationToken)
    {
        Stop();
        _weekend = weekend;
        _circuit = circuit;
        _session = await _apiClient.GetPreferredRaceSessionAsync(weekend, cancellationToken);
        _driversByNumber.Clear();
        _lastSnapshotsByNumber.Clear();
        _currentTransform = null;

        if (_session is null)
        {
            LastStatusMessage = "No race session was available from OpenF1 for this weekend.";
            return false;
        }

        var liveDrivers = await _apiClient.GetSessionDriversAsync(_session.SessionKey, cancellationToken);
        if (liveDrivers.Count == 0)
        {
            LastStatusMessage = $"OpenF1 returned no driver roster for {_session.SessionName}.";
            return false;
        }

        foreach (var driver in CircuitCatalog.CreateLiveGrid(liveDrivers))
        {
            if (driver.DriverNumber is int driverNumber)
            {
                _driversByNumber[driverNumber] = driver;
            }
        }

        var initialSnapshots = await PollOnceAsync(cancellationToken);
        if (initialSnapshots.Count == 0)
        {
            LastStatusMessage = "The race session was found, but no live driver packets were available yet.";
            return false;
        }

        _timer.Start();
        LastStatusMessage = $"Live {_session.SessionName.ToLowerInvariant()} timing is connected for {weekend.GrandPrixName}.";
        return true;
    }

    public void Stop()
    {
        if (_timer.IsEnabled)
        {
            _timer.Stop();
        }
    }

    public void Dispose()
    {
        Stop();
        _timer.Tick -= OnTick;
        _apiClient.Dispose();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_pollInProgress || _session is null || _circuit is null)
        {
            return;
        }

        _pollInProgress = true;
        try
        {
            await PollOnceAsync(CancellationToken.None);
        }
        finally
        {
            _pollInProgress = false;
        }
    }

    private async Task<IReadOnlyList<DriverSnapshot>> PollOnceAsync(CancellationToken cancellationToken)
    {
        if (_session is null || _circuit is null)
        {
            return Array.Empty<DriverSnapshot>();
        }

        var now = DateTimeOffset.UtcNow;
        var intervalsTask = _apiClient.GetIntervalsAsync(_session.SessionKey, now.AddSeconds(-25), cancellationToken);
        var positionsTask = _apiClient.GetPositionsAsync(_session.SessionKey, now.AddMinutes(-2), cancellationToken);
        var locationsTask = _apiClient.GetLocationsAsync(_session.SessionKey, now.AddSeconds(-18), cancellationToken);
        var carDataTask = _apiClient.GetCarDataAsync(_session.SessionKey, now.AddSeconds(-8), cancellationToken);
        await Task.WhenAll(intervalsTask, positionsTask, locationsTask, carDataTask);

        var intervals = intervalsTask.Result;
        var positions = positionsTask.Result;
        var locations = locationsTask.Result;
        var carData = carDataTask.Result;

        if (locations.Count == 0)
        {
            return Array.Empty<DriverSnapshot>();
        }

        RefreshTransform(locations);

        var latestLocations = locations
            .GroupBy(location => location.DriverNumber)
            .ToDictionary(group => group.Key, group => group.OrderBy(entry => entry.Date).Last());

        var latestIntervals = intervals
            .GroupBy(entry => entry.DriverNumber)
            .ToDictionary(group => group.Key, group => group.OrderBy(entry => entry.Date).Last());

        var latestPositions = positions
            .GroupBy(entry => entry.DriverNumber)
            .ToDictionary(group => group.Key, group => group.OrderBy(entry => entry.Date).Last());

        var latestSpeeds = carData
            .Where(entry => entry.Speed is not null)
            .GroupBy(entry => entry.DriverNumber)
            .ToDictionary(group => group.Key, group => group.OrderBy(entry => entry.Date).Last().Speed ?? 0);

        var ranking = BuildRanking(latestIntervals, latestPositions);
        if (ranking.Count == 0)
        {
            return Array.Empty<DriverSnapshot>();
        }

        var snapshots = new List<DriverSnapshot>(ranking.Count);
        for (var index = 0; index < ranking.Count; index++)
        {
            var entry = ranking[index];
            if (!_driversByNumber.TryGetValue(entry.DriverNumber, out var driver))
            {
                continue;
            }

            var progress = ResolveDriverProgress(entry.DriverNumber, latestLocations);
            var speed = latestSpeeds.TryGetValue(entry.DriverNumber, out var liveSpeed) && liveSpeed > 0
                ? liveSpeed
                : _circuit.AverageSpeedKph;

            var snapshot = new DriverSnapshot(
                driver,
                index + 1,
                0,
                progress,
                speed,
                entry.GapSeconds,
                entry.GapLabel,
                0);

            snapshots.Add(snapshot);
            _lastSnapshotsByNumber[entry.DriverNumber] = snapshot;
        }

        if (snapshots.Count > 0)
        {
            TelemetryUpdated?.Invoke(this, snapshots);
        }

        return snapshots;
    }

    private void RefreshTransform(IReadOnlyList<OpenF1LocationDto> locations)
    {
        if (_circuit is null)
        {
            return;
        }

        var cloud = locations
            .OrderBy(location => location.Date)
            .Select(location => location.ToPoint())
            .Distinct()
            .ToList();

        if (cloud.Count < 12)
        {
            return;
        }

        var candidates = new List<TrackTransform>();
        foreach (var mirrorX in new[] { false, true })
        {
            foreach (var mirrorY in new[] { false, true })
            {
                var transformedInput = ApplyMirror(cloud, mirrorX, mirrorY);
                var sourceAngle = GetPrincipalAngle(transformedInput);
                var targetAngle = GetPrincipalAngle(_circuit.RawTrackPoints);
                var sourceCenter = GetCentroid(cloud);
                var targetCenter = GetCentroid(_circuit.RawTrackPoints);
                var sourceScale = GetRadialScale(transformedInput, GetCentroid(transformedInput));
                var targetScale = GetRadialScale(_circuit.RawTrackPoints, targetCenter);
                var scale = sourceScale <= 0.0001 ? 1.0 : targetScale / sourceScale;

                foreach (var rotation in new[] { targetAngle - sourceAngle, targetAngle - sourceAngle + Math.PI })
                {
                    candidates.Add(new TrackTransform(sourceCenter, targetCenter, rotation, scale, mirrorX, mirrorY));
                }
            }
        }

        var best = candidates
            .Select(candidate => new { Candidate = candidate, Score = ScoreTransform(candidate, cloud) })
            .OrderBy(result => result.Score)
            .FirstOrDefault();

        if (best is not null)
        {
            _currentTransform = best.Candidate;
        }
    }

    private double ResolveDriverProgress(int driverNumber, IReadOnlyDictionary<int, OpenF1LocationDto> latestLocations)
    {
        if (_circuit is null)
        {
            return 0;
        }

        if (_currentTransform is not null && latestLocations.TryGetValue(driverNumber, out var location))
        {
            var projected = _currentTransform.Apply(location.ToPoint());
            if (_circuit.TryProjectRawPointToProgress(projected, out var progress, out _))
            {
                if (_lastSnapshotsByNumber.TryGetValue(driverNumber, out var previous))
                {
                    return SmoothProgress(previous.Progress, progress);
                }

                return progress;
            }
        }

        if (_lastSnapshotsByNumber.TryGetValue(driverNumber, out var fallback))
        {
            return fallback.Progress;
        }

        return 0;
    }

    private List<RankingEntry> BuildRanking(
        IReadOnlyDictionary<int, OpenF1IntervalDto> latestIntervals,
        IReadOnlyDictionary<int, OpenF1PositionDto> latestPositions)
    {
        if (latestIntervals.Count > 0)
        {
            return latestIntervals
                .Select(pair => BuildIntervalRanking(pair.Key, pair.Value))
                .OrderBy(entry => entry.SortGroup)
                .ThenBy(entry => entry.SortValue)
                .ThenBy(entry => entry.DriverNumber)
                .ToList();
        }

        if (latestPositions.Count > 0)
        {
            return latestPositions
                .Select(pair => new RankingEntry(pair.Key, pair.Value.Position, (pair.Value.Position - 1) * 2.5, pair.Value.Position == 1 ? "Leader" : $"+{((pair.Value.Position - 1) * 2.5):0.0}s", 0, pair.Value.Position))
                .OrderBy(entry => entry.Position)
                .ToList();
        }

        return new List<RankingEntry>();
    }

    private static RankingEntry BuildIntervalRanking(int driverNumber, OpenF1IntervalDto interval)
    {
        var (gapLabel, gapSeconds, sortGroup, sortValue) = ParseGap(interval.GapToLeader);
        var position = sortGroup == 0 ? 1 : 0;
        return new RankingEntry(driverNumber, position, gapSeconds, gapLabel, sortGroup, sortValue);
    }

    private static (string GapLabel, double GapSeconds, int SortGroup, double SortValue) ParseGap(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return ("Leader", 0, 0, 0);
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var numericGap))
        {
            return ($"+{numericGap:0.0}s", numericGap, 1, numericGap);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString() ?? string.Empty;
            if (double.TryParse(text.TrimStart('+'), out var parsedGap))
            {
                return ($"+{parsedGap:0.0}s", parsedGap, 1, parsedGap);
            }

            if (text.Contains("LAP", StringComparison.OrdinalIgnoreCase))
            {
                var lapCount = ExtractLeadingNumber(text);
                return (text.Trim(), lapCount * 90.0, 2, lapCount);
            }

            return (text.Trim(), 0, 3, double.MaxValue / 2);
        }

        return ("Live", 0, 3, double.MaxValue);
    }

    private double ScoreTransform(TrackTransform transform, IReadOnlyList<Point> liveCloud)
    {
        if (_circuit is null)
        {
            return double.MaxValue;
        }

        double total = 0;
        var sampleCount = 0;
        var step = Math.Max(1, liveCloud.Count / 180);
        for (var index = 0; index < liveCloud.Count; index += step)
        {
            var transformed = transform.Apply(liveCloud[index]);
            if (_circuit.TryProjectRawPointToProgress(transformed, out _, out var distance))
            {
                total += distance;
                sampleCount++;
            }
        }

        return sampleCount == 0 ? double.MaxValue : total / sampleCount;
    }

    private static double SmoothProgress(double previous, double current)
    {
        var delta = current - previous;
        if (delta < -0.5)
        {
            current += 1.0;
        }
        else if (delta > 0.5)
        {
            current -= 1.0;
        }

        var blended = previous + ((current - previous) * 0.65);
        blended %= 1.0;
        if (blended < 0)
        {
            blended += 1.0;
        }

        return blended;
    }

    private static List<Point> ApplyMirror(IReadOnlyList<Point> points, bool mirrorX, bool mirrorY)
    {
        var center = GetCentroid(points);
        return points.Select(point => new Point(
            center.X + ((point.X - center.X) * (mirrorX ? -1 : 1)),
            center.Y + ((point.Y - center.Y) * (mirrorY ? -1 : 1)))).ToList();
    }

    private static Point GetCentroid(IReadOnlyList<Point> points)
        => new(points.Average(point => point.X), points.Average(point => point.Y));

    private static double GetRadialScale(IReadOnlyList<Point> points, Point center)
    {
        var total = 0.0;
        foreach (var point in points)
        {
            total += Math.Sqrt(Math.Pow(point.X - center.X, 2) + Math.Pow(point.Y - center.Y, 2));
        }

        return points.Count == 0 ? 0 : total / points.Count;
    }

    private static double GetPrincipalAngle(IReadOnlyList<Point> points)
    {
        var center = GetCentroid(points);
        var covXx = 0.0;
        var covYy = 0.0;
        var covXy = 0.0;

        foreach (var point in points)
        {
            var dx = point.X - center.X;
            var dy = point.Y - center.Y;
            covXx += dx * dx;
            covYy += dy * dy;
            covXy += dx * dy;
        }

        return 0.5 * Math.Atan2(2 * covXy, covXx - covYy);
    }

    private static double ExtractLeadingNumber(string text)
    {
        var digits = new string(text.Where(character => char.IsDigit(character) || character == '.').ToArray());
        return double.TryParse(digits, out var parsed) ? parsed : 1;
    }

    private sealed record RankingEntry(int DriverNumber, int Position, double GapSeconds, string GapLabel, int SortGroup, double SortValue);

    private sealed record TrackTransform(Point SourceCenter, Point TargetCenter, double RotationRadians, double Scale, bool MirrorX, bool MirrorY)
    {
        public Point Apply(Point source)
        {
            var dx = source.X - SourceCenter.X;
            var dy = source.Y - SourceCenter.Y;
            if (MirrorX)
            {
                dx *= -1;
            }

            if (MirrorY)
            {
                dy *= -1;
            }

            var rotatedX = (dx * Math.Cos(RotationRadians)) - (dy * Math.Sin(RotationRadians));
            var rotatedY = (dx * Math.Sin(RotationRadians)) + (dy * Math.Cos(RotationRadians));
            return new Point(TargetCenter.X + (rotatedX * Scale), TargetCenter.Y + (rotatedY * Scale));
        }
    }
}
