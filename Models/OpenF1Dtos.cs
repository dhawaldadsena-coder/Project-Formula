using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace F1TrackMapper.Models;

public sealed class OpenF1MeetingDto
{
    [JsonPropertyName("meeting_key")]
    public int MeetingKey { get; init; }

    [JsonPropertyName("meeting_name")]
    public string MeetingName { get; init; } = string.Empty;

    [JsonPropertyName("meeting_official_name")]
    public string MeetingOfficialName { get; init; } = string.Empty;

    [JsonPropertyName("location")]
    public string Location { get; init; } = string.Empty;

    [JsonPropertyName("country_name")]
    public string CountryName { get; init; } = string.Empty;

    [JsonPropertyName("circuit_key")]
    public int CircuitKey { get; init; }

    [JsonPropertyName("circuit_short_name")]
    public string CircuitShortName { get; init; } = string.Empty;

    [JsonPropertyName("circuit_info_url")]
    public string CircuitInfoUrl { get; init; } = string.Empty;

    [JsonPropertyName("circuit_image")]
    public string? CircuitImageUrl { get; init; }

    [JsonPropertyName("date_start")]
    public DateTimeOffset DateStart { get; init; }

    [JsonPropertyName("date_end")]
    public DateTimeOffset DateEnd { get; init; }

    [JsonPropertyName("year")]
    public int Year { get; init; }
}

public sealed class OpenF1SessionDto
{
    [JsonPropertyName("session_key")]
    public int SessionKey { get; init; }

    [JsonPropertyName("meeting_key")]
    public int MeetingKey { get; init; }

    [JsonPropertyName("session_name")]
    public string SessionName { get; init; } = string.Empty;

    [JsonPropertyName("session_type")]
    public string SessionType { get; init; } = string.Empty;

    [JsonPropertyName("date_start")]
    public DateTimeOffset DateStart { get; init; }

    [JsonPropertyName("date_end")]
    public DateTimeOffset? DateEnd { get; init; }
}

public sealed class OpenF1DriverDto
{
    [JsonPropertyName("driver_number")]
    public int DriverNumber { get; init; }

    [JsonPropertyName("name_acronym")]
    public string NameAcronym { get; init; } = string.Empty;

    [JsonPropertyName("full_name")]
    public string FullName { get; init; } = string.Empty;

    [JsonPropertyName("team_name")]
    public string TeamName { get; init; } = string.Empty;

    [JsonPropertyName("team_colour")]
    public string TeamColour { get; init; } = string.Empty;
}

public sealed class OpenF1LocationDto
{
    [JsonPropertyName("date")]
    public DateTimeOffset Date { get; init; }

    [JsonPropertyName("driver_number")]
    public int DriverNumber { get; init; }

    [JsonPropertyName("x")]
    public double X { get; init; }

    [JsonPropertyName("y")]
    public double Y { get; init; }

    [JsonPropertyName("z")]
    public double Z { get; init; }

    public Point ToPoint() => new(X, Y);
}

public sealed class OpenF1PositionDto
{
    [JsonPropertyName("date")]
    public DateTimeOffset Date { get; init; }

    [JsonPropertyName("driver_number")]
    public int DriverNumber { get; init; }

    [JsonPropertyName("position")]
    public int Position { get; init; }
}

public sealed class OpenF1IntervalDto
{
    [JsonPropertyName("date")]
    public DateTimeOffset Date { get; init; }

    [JsonPropertyName("driver_number")]
    public int DriverNumber { get; init; }

    [JsonPropertyName("interval")]
    public JsonElement Interval { get; init; }

    [JsonPropertyName("gap_to_leader")]
    public JsonElement GapToLeader { get; init; }
}

public sealed class OpenF1CarDataDto
{
    [JsonPropertyName("date")]
    public DateTimeOffset Date { get; init; }

    [JsonPropertyName("driver_number")]
    public int DriverNumber { get; init; }

    [JsonPropertyName("speed")]
    public int? Speed { get; init; }
}

public sealed class MultiViewerCircuitInfoDto
{
    [JsonPropertyName("circuitKey")]
    public int CircuitKey { get; init; }

    [JsonPropertyName("circuitName")]
    public string CircuitName { get; init; } = string.Empty;

    [JsonPropertyName("location")]
    public string Location { get; init; } = string.Empty;

    [JsonPropertyName("rotation")]
    public double Rotation { get; init; }

    [JsonPropertyName("x")]
    public List<double> X { get; init; } = new();

    [JsonPropertyName("y")]
    public List<double> Y { get; init; } = new();

    [JsonPropertyName("corners")]
    public List<MultiViewerMarkerDto> Corners { get; init; } = new();

    [JsonPropertyName("marshalSectors")]
    public List<MultiViewerMarkerDto> MarshalSectors { get; init; } = new();

    [JsonPropertyName("candidateLap")]
    public CandidateLapDto? CandidateLap { get; init; }
}

public sealed class MultiViewerMarkerDto
{
    [JsonPropertyName("number")]
    public int Number { get; init; }

    [JsonPropertyName("angle")]
    public double Angle { get; init; }

    [JsonPropertyName("trackPosition")]
    public MultiViewerTrackPointDto TrackPosition { get; init; } = new();
}

public sealed class MultiViewerTrackPointDto
{
    [JsonPropertyName("x")]
    public double X { get; init; }

    [JsonPropertyName("y")]
    public double Y { get; init; }

    public Point ToPoint() => new(X, Y);
}

public sealed class CandidateLapDto
{
    [JsonPropertyName("lapTime")]
    public double? LapTime { get; init; }
}

public sealed class GeoJsonFeatureCollectionDto
{
    [JsonPropertyName("features")]
    public List<GeoJsonCircuitFeatureDto> Features { get; init; } = new();
}

public sealed class GeoJsonCircuitFeatureDto
{
    [JsonPropertyName("properties")]
    public GeoJsonCircuitPropertiesDto Properties { get; init; } = new();

    [JsonPropertyName("geometry")]
    public GeoJsonGeometryDto Geometry { get; init; } = new();
}

public sealed class GeoJsonCircuitPropertiesDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("Location")]
    public string Location { get; init; } = string.Empty;

    [JsonPropertyName("Name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("length")]
    public double? LengthMeters { get; init; }
}

public sealed class GeoJsonGeometryDto
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("coordinates")]
    public List<List<double>> Coordinates { get; init; } = new();

    public IReadOnlyList<Point> ToPoints()
        => Coordinates
            .Where(pair => pair.Count >= 2)
            .Select(pair => new Point(pair[0], pair[1]))
            .ToList();
}
