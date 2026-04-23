using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using F1TrackMapper.Models;
using F1TrackMapper.Services;

namespace F1TrackMapper.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private const double TrackWidth = 960;
    private const double TrackHeight = 620;
    private const double MinMapZoom = 0.75;
    private const double MaxMapZoom = 1.85;
    private const double DefaultMapZoom = 1.0;

    private readonly OpenF1ApiClient _apiClient;
    private readonly MockLiveTimingService _mockLiveTimingService;
    private readonly OpenF1LiveTimingService _openF1LiveTimingService;
    private readonly CancellationTokenSource _disposeCts = new();

    private CircuitDefinition? _activeCircuit;
    private CalendarEntry? _selectedWeekend;
    private Geometry? _trackGeometry;
    private Geometry? _pitLaneGeometry;
    private Brush _accentBrush = CreateBrush("#E10600");
    private string _focusWeekendName = "Loading calendar...";
    private string _focusWeekendDate = string.Empty;
    private string _calendarSelectionNote = "Fetching real 2026 circuit data...";
    private string _selectedCircuitName = "Waiting for circuit metadata";
    private string _selectedCircuitSummary = "The hand-drawn placeholders are being replaced with real circuit geometry.";
    private string _trackStatsLine = string.Empty;
    private string _statusText = "Loading actual circuit geometry from live metadata and fallback GeoJSON sources.";
    private string _feedStatusBadge = "REAL MAP LOADING";
    private string _telemetryModeText = "Map geometry is real. Driver motion remains demo until the live timing adapter is wired in.";
    private string _footerHint = string.Empty;
    private string _lastUpdatedLabel = string.Empty;
    private double _mapZoom = DefaultMapZoom;
    private bool _suppressSelectionApply;
    private bool _isLiveTimingActive;
    private bool _isDemoModeForced;
    private bool _isTelemetryToggleBusy;

    public MainViewModel()
    {
        Weekends = new ObservableCollection<CalendarEntry>();
        TurnMarkers = new ObservableCollection<MarkerDisplayState>();
        SectorMarkers = new ObservableCollection<MarkerDisplayState>();
        SectorCards = new ObservableCollection<SectorCardState>();
        Drivers = new ObservableCollection<DriverDisplayState>();

        _apiClient = new OpenF1ApiClient();
        _mockLiveTimingService = new MockLiveTimingService();
        _mockLiveTimingService.LoadGrid(CircuitCatalog.CreateOfficial2026Grid());
        _mockLiveTimingService.TelemetryUpdated += OnTelemetryUpdated;

        _openF1LiveTimingService = new OpenF1LiveTimingService();
        _openF1LiveTimingService.TelemetryUpdated += OnTelemetryUpdated;

        _ = InitializeAsync(_disposeCts.Token);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<CalendarEntry> Weekends { get; }
    public ObservableCollection<MarkerDisplayState> TurnMarkers { get; }
    public ObservableCollection<MarkerDisplayState> SectorMarkers { get; }
    public ObservableCollection<SectorCardState> SectorCards { get; }
    public ObservableCollection<DriverDisplayState> Drivers { get; }

    public CalendarEntry? SelectedWeekend
    {
        get => _selectedWeekend;
        set
        {
            if (Equals(value, _selectedWeekend))
            {
                return;
            }

            _selectedWeekend = value;
            OnPropertyChanged();

            if (!_suppressSelectionApply && value is not null)
            {
                _ = LoadActualCircuitAsync(value, autoMode: false, _disposeCts.Token);
            }
        }
    }

    public Geometry? TrackGeometry
    {
        get => _trackGeometry;
        private set => SetField(ref _trackGeometry, value);
    }

    public Geometry? PitLaneGeometry
    {
        get => _pitLaneGeometry;
        private set => SetField(ref _pitLaneGeometry, value);
    }

    public Brush AccentBrush
    {
        get => _accentBrush;
        private set => SetField(ref _accentBrush, value);
    }

    public string FocusWeekendName
    {
        get => _focusWeekendName;
        private set => SetField(ref _focusWeekendName, value);
    }

    public string FocusWeekendDate
    {
        get => _focusWeekendDate;
        private set => SetField(ref _focusWeekendDate, value);
    }

    public string CalendarSelectionNote
    {
        get => _calendarSelectionNote;
        private set => SetField(ref _calendarSelectionNote, value);
    }

    public string SelectedCircuitName
    {
        get => _selectedCircuitName;
        private set => SetField(ref _selectedCircuitName, value);
    }

    public string SelectedCircuitSummary
    {
        get => _selectedCircuitSummary;
        private set => SetField(ref _selectedCircuitSummary, value);
    }

    public string TrackStatsLine
    {
        get => _trackStatsLine;
        private set => SetField(ref _trackStatsLine, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string FeedStatusBadge
    {
        get => _feedStatusBadge;
        private set => SetField(ref _feedStatusBadge, value);
    }

    public string TelemetryModeText
    {
        get => _telemetryModeText;
        private set => SetField(ref _telemetryModeText, value);
    }

    public string FooterHint
    {
        get => _footerHint;
        private set => SetField(ref _footerHint, value);
    }

    public string LastUpdatedLabel
    {
        get => _lastUpdatedLabel;
        private set => SetField(ref _lastUpdatedLabel, value);
    }

    public bool IsDemoModeForced
    {
        get => _isDemoModeForced;
        private set
        {
            if (_isDemoModeForced == value)
            {
                return;
            }

            _isDemoModeForced = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TelemetryToggleLabel));
            OnPropertyChanged(nameof(TelemetryToggleCaption));
        }
    }

    public string TelemetryToggleLabel
    {
        get
        {
            if (_isTelemetryToggleBusy)
            {
                return "Switching...";
            }

            return IsDemoModeForced ? "Switch To Live" : "Switch To Demo";
        }
    }

    public string TelemetryToggleCaption
        => IsDemoModeForced
            ? "Demo mode is locked on. Click to retry the real live feed for this weekend."
            : "Live timing is preferred when available. Click to force the old demo motion.";

    public double MapZoom
    {
        get => _mapZoom;
        set
        {
            var clamped = Math.Max(MinMapZoom, Math.Min(MaxMapZoom, value));
            if (Math.Abs(_mapZoom - clamped) < 0.001)
            {
                return;
            }

            _mapZoom = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MapZoomLabel));
        }
    }

    public string MapZoomLabel => $"{MapZoom * 100:0}%";

    public void ZoomIn() => MapZoom += 0.1;

    public void ZoomOut() => MapZoom -= 0.1;

    public void ResetZoom() => MapZoom = DefaultMapZoom;

    public async Task ToggleTelemetryModeAsync()
    {
        if (_selectedWeekend is null || _isTelemetryToggleBusy)
        {
            return;
        }

        _isTelemetryToggleBusy = true;
        OnPropertyChanged(nameof(TelemetryToggleLabel));

        try
        {
            IsDemoModeForced = !IsDemoModeForced;
            await LoadActualCircuitAsync(_selectedWeekend, autoMode: false, _disposeCts.Token);
        }
        finally
        {
            _isTelemetryToggleBusy = false;
            OnPropertyChanged(nameof(TelemetryToggleLabel));
            OnPropertyChanged(nameof(TelemetryToggleCaption));
        }
    }

    public void Dispose()
    {
        _disposeCts.Cancel();
        _mockLiveTimingService.TelemetryUpdated -= OnTelemetryUpdated;
        _openF1LiveTimingService.TelemetryUpdated -= OnTelemetryUpdated;
        _mockLiveTimingService.Dispose();
        _openF1LiveTimingService.Dispose();
        _apiClient.Dispose();
        _disposeCts.Dispose();
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var calendar = await _apiClient.GetRaceCalendarAsync(DateTime.Today.Year, cancellationToken);
            foreach (var weekend in calendar)
            {
                Weekends.Add(weekend);
            }

            if (Weekends.Count == 0)
            {
                StatusText = "No race meetings were returned for the current season.";
                FeedStatusBadge = "NO CIRCUITS";
                return;
            }

            var autoSelection = SelectWeekendForDate(DateTimeOffset.Now, Weekends);
            _suppressSelectionApply = true;
            SelectedWeekend = autoSelection;
            _suppressSelectionApply = false;
            await LoadActualCircuitAsync(autoSelection, autoMode: true, cancellationToken);
        }
        catch (Exception ex)
        {
            StatusText = $"Could not load real circuit data: {ex.Message}";
            FeedStatusBadge = "MAP LOAD FAILED";
            SelectedCircuitSummary = "The app could not reach the circuit data sources, so the real track outline could not be loaded.";
        }
    }

    private async Task LoadActualCircuitAsync(CalendarEntry weekend, bool autoMode, CancellationToken cancellationToken)
    {
        try
        {
            _mockLiveTimingService.Stop();
            _openF1LiveTimingService.Stop();
            Drivers.Clear();
            FeedStatusBadge = "REAL MAP LOADING";
            StatusText = $"Loading the actual {weekend.CircuitShortName} circuit outline from live metadata first, with GeoJSON fallback if needed.";

            var circuitInfo = await _apiClient.GetCircuitInfoAsync(weekend.CircuitInfoUrl, cancellationToken);
            var geoCircuit = await _apiClient.GetGeoCircuitAsync(weekend, cancellationToken);

            if (circuitInfo is null && geoCircuit is null)
            {
                throw new InvalidOperationException("No circuit layout sources were available.");
            }

            var circuit = ActualCircuitBuilder.Build(weekend, circuitInfo, geoCircuit);
            var usedGeoJson = circuitInfo is null && geoCircuit is not null;
            var liveConnected = false;

            if (!IsDemoModeForced)
            {
                liveConnected = await _openF1LiveTimingService.TryActivateAsync(weekend, circuit, cancellationToken);
            }

            _isLiveTimingActive = liveConnected;

            if (IsDemoModeForced)
            {
                _mockLiveTimingService.SetCircuit(circuit);
                _mockLiveTimingService.Start();
            }

            ApplyCircuit(weekend, circuit, autoMode, usedGeoJson, liveConnected);
            LastUpdatedLabel = liveConnected
                ? $"Live sync {DateTime.Now:HH:mm:ss}"
                : $"Map synced {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not load the actual track map: {ex.Message}";
            FeedStatusBadge = "MAP LOAD FAILED";
        }
    }

    private void ApplyCircuit(CalendarEntry weekend, CircuitDefinition circuit, bool autoMode, bool usedGeoJson, bool liveConnected)
    {
        _activeCircuit = circuit;
        AccentBrush = CreateBrush(circuit.AccentHex);
        TrackGeometry = circuit.CreateTrackGeometry(TrackWidth, TrackHeight);
        PitLaneGeometry = circuit.CreatePitLaneGeometry(TrackWidth, TrackHeight);

        FocusWeekendName = weekend.GrandPrixName;
        FocusWeekendDate = $"{weekend.DateLabel}  •  {weekend.Location}, {weekend.CountryName}";
        CalendarSelectionNote = autoMode
            ? "Auto-loaded with real circuit geometry for the current season."
            : "Manual preview is using real circuit geometry.";

        SelectedCircuitName = circuit.DisplayName;
        SelectedCircuitSummary = circuit.Summary;
        var turnCount = circuit.Markers.Count(marker => marker.Kind == MarkerKind.Turn);
        var sectorCount = circuit.Markers.Count(marker => marker.Kind == MarkerKind.Sector);
        TrackStatsLine = $"Actual corners: {turnCount}  •  Marshal sectors: {sectorCount}  •  Pit lane overlay: {(PitLaneGeometry is null ? "Unavailable" : "Visible")}";
        StatusText = usedGeoJson
            ? "The circuit outline is using bacinger/f1-circuits GeoJSON because live metadata was unavailable for this round."
            : "The circuit outline now comes from the live circuit metadata source.";

        if (liveConnected)
        {
            FeedStatusBadge = usedGeoJson ? "GEOJSON MAP · LIVE" : "REAL MAP · LIVE";
            TelemetryModeText = _openF1LiveTimingService.LastStatusMessage;
            FooterHint = "Cars on the track are now driven by OpenF1 live timing packets when an active race session is available.";
        }
        else if (IsDemoModeForced)
        {
            FeedStatusBadge = usedGeoJson ? "GEOJSON MAP · DEMO MODE" : "REAL MAP · DEMO MODE";
            TelemetryModeText = "Demo mode is manually enabled, so the track uses the previous simulated car motion.";
            FooterHint = "The app is intentionally using demo motion on the real map until you switch live timing back on.";
        }
        else
        {
            FeedStatusBadge = usedGeoJson ? "GEOJSON MAP · WAITING LIVE" : "REAL MAP · WAITING LIVE";
            TelemetryModeText = _openF1LiveTimingService.LastStatusMessage;
            FooterHint = "Live mode is on, but the OpenF1 race feed is not available yet for this session. Switch to Demo if you want the simulated motion.";
        }

        BuildTrackMarkers(circuit, usedGeoJson);
    }

    private void BuildTrackMarkers(CircuitDefinition circuit, bool usedGeoJson)
    {
        TurnMarkers.Clear();
        SectorMarkers.Clear();
        SectorCards.Clear();

        foreach (var marker in circuit.Markers.Where(marker => marker.Kind == MarkerKind.Turn))
        {
            var point = circuit.MapMarkerPoint(marker.TrackPoint, TrackWidth, TrackHeight);
            var offset = GetOffset(marker.Angle, 20);
            TurnMarkers.Add(new MarkerDisplayState(
                marker.Title,
                marker.Subtitle,
                point.X + offset.X,
                point.Y + offset.Y,
                CreateBrush("#F6F9FD")));
        }

        foreach (var marker in circuit.Markers.Where(marker => marker.Kind is MarkerKind.Sector or MarkerKind.Pit))
        {
            var point = circuit.MapMarkerPoint(marker.TrackPoint, TrackWidth, TrackHeight);
            var offset = marker.Kind == MarkerKind.Pit ? new Vector(26, -20) : GetOffset(marker.Angle, 8);
            var brushHex = marker.Kind == MarkerKind.Pit ? "#FFD166" : circuit.AccentHex;
            SectorMarkers.Add(new MarkerDisplayState(
                marker.Title,
                marker.Subtitle,
                point.X + offset.X,
                point.Y + offset.Y,
                CreateBrush(brushHex)));
        }

        SectorCards.Add(new SectorCardState(
            usedGeoJson ? "GEO" : "TRN",
            usedGeoJson ? "GeoJSON layout active" : $"{circuit.Markers.Count(marker => marker.Kind == MarkerKind.Turn)} turn markers",
            usedGeoJson ? "Track shape comes from bacinger/f1-circuits when live metadata is missing." : "Corner callouts come from the live circuit metadata positions.",
            CreateBrush(circuit.AccentHex)));
        SectorCards.Add(new SectorCardState(
            "SEC",
            $"{circuit.Markers.Count(marker => marker.Kind == MarkerKind.Sector)} sector markers",
            usedGeoJson ? "Sector markers appear when compatible live metadata is also available." : "Sector markers use the live circuit metadata coordinates.",
            CreateBrush(circuit.AccentHex)));
        SectorCards.Add(new SectorCardState(
            _isLiveTimingActive ? "LIVE" : "DEMO",
            _isLiveTimingActive ? "OpenF1 live telemetry active" : IsDemoModeForced ? "Manual demo mode active" : "Live mode waiting",
            _isLiveTimingActive
                ? "The driver chips and leaderboard are updating from the active race session feed."
                : IsDemoModeForced
                    ? "You forced the app back to demo mode, so the previous simulated motion is running on top of the real circuit."
                    : "The app is keeping the real circuit loaded while it waits for usable OpenF1 live race packets.",
            CreateBrush(_isLiveTimingActive ? "#7AE582" : "#FFD166")));
    }

    private void OnTelemetryUpdated(object? sender, IReadOnlyList<DriverSnapshot> snapshots)
    {
        if (_activeCircuit is null)
        {
            return;
        }

        Drivers.Clear();

        foreach (var snapshot in snapshots)
        {
            var point = _activeCircuit.GetCanvasPoint(snapshot.Progress, TrackWidth, TrackHeight, snapshot.LaneOffset);
            var accent = CreateBrush(snapshot.Driver.AccentHex);
            var teamLine = _isLiveTimingActive
                ? $"{snapshot.Driver.TeamName}  •  Live timing"
                : $"{snapshot.Driver.TeamName}  •  Demo motion";
            var speedText = _isLiveTimingActive
                ? $"{snapshot.SpeedKph:0} km/h"
                : "Real map";

            Drivers.Add(new DriverDisplayState(
                snapshot.Position,
                snapshot.Driver.Code,
                snapshot.Driver.TeamBadge,
                snapshot.Driver.TeamLogoPath,
                snapshot.Driver.FullName,
                snapshot.Driver.TeamName,
                $"{snapshot.Driver.FullName}  •  {snapshot.Driver.Code}",
                teamLine,
                snapshot.GapLabel,
                speedText,
                Math.Max(0, point.X - 18),
                Math.Max(0, point.Y - 10),
                accent,
                CreateSurfaceBrush(snapshot.Driver.AccentHex, 0.28),
                CreateSurfaceBrush(snapshot.Driver.AccentHex, 0.55),
                CreateBrush("#F8FBFF"),
                CreateBrush("#D2E0ED")));
        }

        LastUpdatedLabel = _isLiveTimingActive
            ? $"Live updated {DateTime.Now:HH:mm:ss}"
            : $"Updated {DateTime.Now:HH:mm:ss}";
    }

    private static CalendarEntry SelectWeekendForDate(DateTimeOffset today, IEnumerable<CalendarEntry> weekends)
    {
        var list = weekends.ToList();
        var current = list.FirstOrDefault(weekend => today >= weekend.StartDate && today <= weekend.EndDate.AddDays(1));
        if (current is not null)
        {
            return current;
        }

        var recent = list
            .Where(weekend => today > weekend.EndDate && (today - weekend.EndDate).TotalDays <= 7)
            .OrderBy(weekend => today - weekend.EndDate)
            .FirstOrDefault();

        if (recent is not null)
        {
            return recent;
        }

        var next = list
            .Where(weekend => weekend.StartDate >= today)
            .OrderBy(weekend => weekend.StartDate)
            .FirstOrDefault();

        return next ?? list.Last();
    }

    private static Vector GetOffset(double angleDegrees, double distance)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Vector(Math.Cos(radians) * distance, -Math.Sin(radians) * distance);
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush CreateSurfaceBrush(string hex, double opacity)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var mixed = Color.FromArgb(
            (byte)Math.Round(255 * opacity),
            (byte)Math.Round((color.R * 0.72) + 18),
            (byte)Math.Round((color.G * 0.72) + 18),
            (byte)Math.Round((color.B * 0.72) + 18));
        var brush = new SolidColorBrush(mixed);
        brush.Freeze();
        return brush;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record MarkerDisplayState(
    string Title,
    string Subtitle,
    double X,
    double Y,
    Brush AccentBrush);

public sealed record SectorCardState(
    string Title,
    string Subtitle,
    string Description,
    Brush AccentBrush);

public sealed record DriverDisplayState(
    int Position,
    string Code,
    string TeamBadge,
    string TeamLogoPath,
    string FullName,
    string TeamName,
    string DriverLine,
    string TeamLine,
    string GapText,
    string SpeedText,
    double X,
    double Y,
    Brush AccentBrush,
    Brush SurfaceBrush,
    Brush LogoPlateBrush,
    Brush PrimaryTextBrush,
    Brush SecondaryTextBrush)
{
    public string PositionLabel => $"P{Position}";
}

