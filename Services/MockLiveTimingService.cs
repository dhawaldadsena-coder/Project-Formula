using System.Windows.Threading;
using F1TrackMapper.Models;

namespace F1TrackMapper.Services;

public sealed class MockLiveTimingService : IDisposable
{
    private static readonly Dictionary<string, int> PreferredDemoOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VER"] = 0,
        ["LEC"] = 1,
        ["ANT"] = 2,
        ["NOR"] = 3
    };

    private readonly DispatcherTimer _timer;
    private readonly Random _random = new();
    private readonly List<LiveDriverState> _states = new();
    private CircuitDefinition? _circuit;

    public MockLiveTimingService()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(260)
        };
        _timer.Tick += OnTick;
    }

    public event EventHandler<IReadOnlyList<DriverSnapshot>>? TelemetryUpdated;

    public void LoadGrid(IReadOnlyList<DriverDefinition> drivers)
    {
        _states.Clear();
        for (var index = 0; index < drivers.Count; index++)
        {
            _states.Add(new LiveDriverState(
                drivers[index],
                16 + _random.Next(2, 8),
                _random.NextDouble(),
                _random.NextDouble() * 0.92,
                -26 + (_random.NextDouble() * 52)));
        }

        ApplyDemoStartingOrder();
    }

    public void SetCircuit(CircuitDefinition circuit)
    {
        _circuit = circuit;
    }

    public void Start()
    {
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
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
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_circuit is null || _states.Count == 0)
        {
            return;
        }

        foreach (var state in _states)
        {
            var paceDelta = 0.00245 + (state.Driver.PaceBias * 0.00028) + (_random.NextDouble() * 0.00040);
            state.Progress += paceDelta;

            if (state.Progress >= 1.0)
            {
                state.Progress -= 1.0;
                state.CompletedLaps++;
            }

            state.SpeedKph = _circuit.AverageSpeedKph * (0.972 + (_random.NextDouble() * 0.038));
        }

        var ordered = _states
            .OrderBy(state => GetDemoSortRank(state))
            .ThenByDescending(state => state.CompletedLaps + state.Progress)
            .ToList();

        var leaderDistance = ordered[0].CompletedLaps + ordered[0].Progress;
        var snapshots = new List<DriverSnapshot>(ordered.Count);

        for (var index = 0; index < ordered.Count; index++)
        {
            var state = ordered[index];
            var gapSeconds = (leaderDistance - (state.CompletedLaps + state.Progress)) * _circuit.ReferenceLapSeconds;
            snapshots.Add(new DriverSnapshot(
                state.Driver,
                index + 1,
                state.CompletedLaps,
                state.Progress,
                state.SpeedKph,
                Math.Max(0, gapSeconds),
                index == 0 ? "Leader" : $"+{Math.Max(0, gapSeconds):0.0}s",
                state.LaneOffset));
        }

        TelemetryUpdated?.Invoke(this, snapshots);
    }

    private void ApplyDemoStartingOrder()
    {
        const int baseLaps = 25;
        const double leadProgress = 0.965;

        foreach (var state in _states)
        {
            if (PreferredDemoOrder.TryGetValue(state.Driver.Code, out var rank))
            {
                state.CompletedLaps = baseLaps;
                state.Progress = Math.Max(0.01, leadProgress - (rank * 0.015));
            }
            else
            {
                state.CompletedLaps = baseLaps - 1;
                state.Progress = 0.10 + (_random.NextDouble() * 0.75);
            }
        }
    }

    private static int GetDemoSortRank(LiveDriverState state)
    {
        return PreferredDemoOrder.TryGetValue(state.Driver.Code, out var rank)
            ? rank
            : 100;
    }

    private sealed class LiveDriverState
    {
        public LiveDriverState(DriverDefinition driver, int completedLaps, double progress, double speedKph, double laneOffset)
        {
            Driver = driver;
            CompletedLaps = completedLaps;
            Progress = progress;
            SpeedKph = speedKph;
            LaneOffset = laneOffset;
        }

        public DriverDefinition Driver { get; }
        public int CompletedLaps { get; set; }
        public double Progress { get; set; }
        public double SpeedKph { get; set; }
        public double LaneOffset { get; }
    }
}
