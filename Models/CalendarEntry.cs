namespace F1TrackMapper.Models;

public sealed record CalendarEntry(
    int Round,
    int MeetingKey,
    int CircuitKey,
    string GrandPrixName,
    string OfficialName,
    string CircuitShortName,
    string Location,
    string CountryName,
    string CircuitInfoUrl,
    string? CircuitImageUrl,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    int Year)
{
    public string DisplayName => $"Round {Round:00}  •  {GrandPrixName}";
    public string DateLabel => $"{StartDate:dd MMM} - {EndDate:dd MMM yyyy}";
}
