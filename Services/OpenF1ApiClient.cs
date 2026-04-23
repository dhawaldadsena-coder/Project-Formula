using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using F1TrackMapper.Models;

namespace F1TrackMapper.Services;

public sealed class OpenF1ApiClient : IDisposable
{
    private const string CircuitsGeoJsonUrl = "https://raw.githubusercontent.com/bacinger/f1-circuits/master/f1-circuits.geojson";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _openF1Client = new()
    {
        BaseAddress = new Uri("https://api.openf1.org/v1/")
    };

    private readonly HttpClient _multiViewerClient = new();
    private IReadOnlyList<GeoJsonCircuitFeatureDto>? _cachedGeoCircuits;

    public async Task<IReadOnlyList<CalendarEntry>> GetRaceCalendarAsync(int year, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _openF1Client.GetAsync($"meetings?year={year}", cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var meetings = await JsonSerializer.DeserializeAsync<List<OpenF1MeetingDto>>(stream, JsonOptions, cancellationToken)
                ?? new List<OpenF1MeetingDto>();

            var raceMeetings = meetings
                .Where(meeting => meeting.MeetingOfficialName.Contains("GRAND PRIX", StringComparison.OrdinalIgnoreCase))
                .OrderBy(meeting => meeting.DateStart)
                .ToList();

            var calendar = new List<CalendarEntry>(raceMeetings.Count);
            for (var index = 0; index < raceMeetings.Count; index++)
            {
                var meeting = raceMeetings[index];
                calendar.Add(new CalendarEntry(
                    index + 1,
                    meeting.MeetingKey,
                    meeting.CircuitKey,
                    meeting.MeetingName,
                    meeting.MeetingOfficialName,
                    meeting.CircuitShortName,
                    meeting.Location,
                    meeting.CountryName,
                    meeting.CircuitInfoUrl,
                    meeting.CircuitImageUrl,
                    meeting.DateStart,
                    meeting.DateEnd,
                    meeting.Year));
            }

            return calendar;
        }
        catch
        {
            return CreateFallbackCalendar(year);
        }
    }

    public async Task<MultiViewerCircuitInfoDto?> GetCircuitInfoAsync(string circuitInfoUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(circuitInfoUrl))
        {
            return null;
        }

        try
        {
            var response = await _multiViewerClient.GetAsync(circuitInfoUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<MultiViewerCircuitInfoDto>(stream, JsonOptions, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<GeoJsonCircuitFeatureDto?> GetGeoCircuitAsync(CalendarEntry weekend, CancellationToken cancellationToken)
    {
        try
        {
            _cachedGeoCircuits ??= await LoadGeoCircuitsAsync(cancellationToken);
        }
        catch
        {
            return null;
        }

        return _cachedGeoCircuits.FirstOrDefault(feature =>
            Matches(feature.Properties.Name, weekend.CircuitShortName) ||
            Matches(feature.Properties.Name, weekend.Location) ||
            Matches(feature.Properties.Location, weekend.Location));
    }

    public async Task<OpenF1SessionDto?> GetPreferredRaceSessionAsync(CalendarEntry weekend, CancellationToken cancellationToken)
    {
        var sessions = await GetSessionsAsync($"sessions?meeting_key={weekend.MeetingKey}", cancellationToken);
        if (sessions.Count == 0)
        {
            sessions = await GetSessionsAsync("sessions?session_key=latest", cancellationToken);
        }

        if (sessions.Count == 0)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var ordered = sessions.OrderBy(session => session.DateStart).ToList();

        var activeRace = ordered.FirstOrDefault(session =>
            IsRaceSession(session) &&
            now >= session.DateStart.AddMinutes(-30) &&
            now <= (session.DateEnd ?? session.DateStart.AddHours(4)).AddMinutes(45));
        if (activeRace is not null)
        {
            return activeRace;
        }

        var latestRace = ordered
            .Where(IsRaceSession)
            .OrderByDescending(session => session.DateStart)
            .FirstOrDefault();

        return latestRace ?? ordered.OrderByDescending(session => session.DateStart).FirstOrDefault();
    }

    public Task<IReadOnlyList<OpenF1DriverDto>> GetSessionDriversAsync(int sessionKey, CancellationToken cancellationToken)
        => QueryOpenF1Async<OpenF1DriverDto>($"drivers?session_key={sessionKey}", cancellationToken);

    public Task<IReadOnlyList<OpenF1IntervalDto>> GetIntervalsAsync(int sessionKey, DateTimeOffset since, CancellationToken cancellationToken)
        => QueryOpenF1Async<OpenF1IntervalDto>($"intervals?session_key={sessionKey}&date>={Uri.EscapeDataString(since.UtcDateTime.ToString("O"))}", cancellationToken);

    public Task<IReadOnlyList<OpenF1PositionDto>> GetPositionsAsync(int sessionKey, DateTimeOffset since, CancellationToken cancellationToken)
        => QueryOpenF1Async<OpenF1PositionDto>($"position?session_key={sessionKey}&date>={Uri.EscapeDataString(since.UtcDateTime.ToString("O"))}", cancellationToken);

    public Task<IReadOnlyList<OpenF1LocationDto>> GetLocationsAsync(int sessionKey, DateTimeOffset since, CancellationToken cancellationToken)
        => QueryOpenF1Async<OpenF1LocationDto>($"location?session_key={sessionKey}&date>={Uri.EscapeDataString(since.UtcDateTime.ToString("O"))}", cancellationToken);

    public Task<IReadOnlyList<OpenF1CarDataDto>> GetCarDataAsync(int sessionKey, DateTimeOffset since, CancellationToken cancellationToken)
        => QueryOpenF1Async<OpenF1CarDataDto>($"car_data?session_key={sessionKey}&date>={Uri.EscapeDataString(since.UtcDateTime.ToString("O"))}", cancellationToken);

    private async Task<IReadOnlyList<OpenF1SessionDto>> GetSessionsAsync(string requestUri, CancellationToken cancellationToken)
        => await QueryOpenF1Async<OpenF1SessionDto>(requestUri, cancellationToken);

    private async Task<IReadOnlyList<T>> QueryOpenF1Async<T>(string requestUri, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _openF1Client.GetAsync(requestUri, cancellationToken);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<List<T>>(JsonOptions, cancellationToken);
            return payload is null ? Array.Empty<T>() : payload;
        }
        catch
        {
            return Array.Empty<T>();
        }
    }

    private async Task<IReadOnlyList<GeoJsonCircuitFeatureDto>> LoadGeoCircuitsAsync(CancellationToken cancellationToken)
    {
        var response = await _multiViewerClient.GetAsync(CircuitsGeoJsonUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var collection = await JsonSerializer.DeserializeAsync<GeoJsonFeatureCollectionDto>(stream, JsonOptions, cancellationToken);
        return collection is null ? Array.Empty<GeoJsonCircuitFeatureDto>() : collection.Features;
    }

    private static bool IsRaceSession(OpenF1SessionDto session)
        => session.SessionName.Contains("Race", StringComparison.OrdinalIgnoreCase)
            || session.SessionType.Contains("Race", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<CalendarEntry> CreateFallbackCalendar(int year)
    {
        if (year != 2026)
        {
            return Array.Empty<CalendarEntry>();
        }

        return new List<CalendarEntry>
        {
            FallbackWeekend(1, 1101, 1, "Australian Grand Prix", "Albert Park", "Melbourne", "Australia", "2026-03-06", "2026-03-08"),
            FallbackWeekend(2, 1102, 2, "Chinese Grand Prix", "Shanghai", "Shanghai", "China", "2026-03-13", "2026-03-15"),
            FallbackWeekend(3, 1103, 3, "Japanese Grand Prix", "Suzuka", "Suzuka", "Japan", "2026-03-27", "2026-03-29"),
            FallbackWeekend(4, 1104, 4, "Miami Grand Prix", "Miami", "Miami", "United States", "2026-05-01", "2026-05-03"),
            FallbackWeekend(5, 1105, 5, "Canadian Grand Prix", "Montreal", "Montreal", "Canada", "2026-05-22", "2026-05-24"),
            FallbackWeekend(6, 1106, 6, "Monaco Grand Prix", "Monte Carlo", "Monte Carlo", "Monaco", "2026-06-05", "2026-06-07"),
            FallbackWeekend(7, 1107, 7, "Barcelona-Catalunya Grand Prix", "Barcelona-Catalunya", "Barcelona", "Spain", "2026-06-12", "2026-06-14"),
            FallbackWeekend(8, 1108, 8, "Austrian Grand Prix", "Austria", "Spielberg", "Austria", "2026-06-26", "2026-06-28"),
            FallbackWeekend(9, 1109, 9, "British Grand Prix", "Silverstone", "Silverstone", "United Kingdom", "2026-07-03", "2026-07-05"),
            FallbackWeekend(10, 1110, 10, "Belgian Grand Prix", "Spa-Francorchamps", "Spa-Francorchamps", "Belgium", "2026-07-17", "2026-07-19"),
            FallbackWeekend(11, 1111, 11, "Hungarian Grand Prix", "Hungaroring", "Budapest", "Hungary", "2026-07-24", "2026-07-26"),
            FallbackWeekend(12, 1112, 12, "Dutch Grand Prix", "Zandvoort", "Zandvoort", "Netherlands", "2026-08-21", "2026-08-23"),
            FallbackWeekend(13, 1113, 13, "Italian Grand Prix", "Monza", "Monza", "Italy", "2026-09-04", "2026-09-06"),
            FallbackWeekend(14, 1114, 14, "Spanish Grand Prix", "Madrid", "Madrid", "Spain", "2026-09-11", "2026-09-13"),
            FallbackWeekend(15, 1115, 15, "Azerbaijan Grand Prix", "Baku", "Baku", "Azerbaijan", "2026-09-24", "2026-09-26"),
            FallbackWeekend(16, 1116, 61, "Singapore Grand Prix", "Singapore", "Marina Bay", "Singapore", "2026-10-09", "2026-10-11"),
            FallbackWeekend(17, 1117, 17, "United States Grand Prix", "Austin", "Austin", "United States", "2026-10-23", "2026-10-25"),
            FallbackWeekend(18, 1118, 18, "Mexico City Grand Prix", "Mexico City", "Mexico City", "Mexico", "2026-10-30", "2026-11-01"),
            FallbackWeekend(19, 1119, 19, "Sao Paulo Grand Prix", "Interlagos", "Sao Paulo", "Brazil", "2026-11-06", "2026-11-08"),
            FallbackWeekend(20, 1120, 20, "Las Vegas Grand Prix", "Las Vegas", "Las Vegas", "United States", "2026-11-19", "2026-11-21"),
            FallbackWeekend(21, 1121, 21, "Qatar Grand Prix", "Lusail", "Lusail", "Qatar", "2026-11-27", "2026-11-29"),
            FallbackWeekend(22, 1122, 22, "Abu Dhabi Grand Prix", "Yas Marina", "Abu Dhabi", "United Arab Emirates", "2026-12-04", "2026-12-06")
        };
    }

    private static CalendarEntry FallbackWeekend(
        int round,
        int meetingKey,
        int circuitKey,
        string grandPrixName,
        string circuitShortName,
        string location,
        string countryName,
        string startDate,
        string endDate)
    {
        var start = DateTimeOffset.Parse($"{startDate}T00:00:00+00:00");
        var end = DateTimeOffset.Parse($"{endDate}T23:59:59+00:00");

        return new CalendarEntry(
            round,
            meetingKey,
            circuitKey,
            grandPrixName,
            grandPrixName,
            circuitShortName,
            location,
            countryName,
            string.Empty,
            null,
            start,
            end,
            2026);
    }

    private static bool Matches(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var normalizedLeft = Normalize(left);
        var normalizedRight = Normalize(right);
        return normalizedLeft.Contains(normalizedRight, StringComparison.OrdinalIgnoreCase)
            || normalizedRight.Contains(normalizedLeft, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
        => value
            .Replace("Circuit", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("International Racing Course", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Autodrome", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Grand Prix", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();

    public void Dispose()
    {
        _openF1Client.Dispose();
        _multiViewerClient.Dispose();
    }
}
