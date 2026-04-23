using F1TrackMapper.Models;

namespace F1TrackMapper.Services;

public static class CircuitCatalog
{
    private static readonly Dictionary<string, TeamStyle> TeamStyles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mercedes"] = new("MER", "mercedes", "#27F4D2"),
        ["Scuderia Ferrari"] = new("FER", "ferrari", "#D5002A"),
        ["Ferrari"] = new("FER", "ferrari", "#D5002A"),
        ["McLaren"] = new("MCL", "mclaren", "#ED7700"),
        ["McLaren Formula 1 Team"] = new("MCL", "mclaren", "#ED7700"),
        ["Oracle Red Bull Racing"] = new("RBR", "redbullracing", "#21477C"),
        ["Red Bull Racing"] = new("RBR", "redbullracing", "#21477C"),
        ["Racing Bulls"] = new("RB", "racingbulls", "#4070E8"),
        ["Visa Cash App RB"] = new("RB", "racingbulls", "#4070E8"),
        ["Visa Cash App Racing Bulls F1 Team"] = new("RB", "racingbulls", "#4070E8"),
        ["Audi"] = new("AUD", "audi", "#CC2400"),
        ["Sauber"] = new("AUD", "audi", "#CC2400"),
        ["Stake F1 Team Kick Sauber"] = new("AUD", "audi", "#CC2400"),
        ["Williams"] = new("WIL", "williams", "#124A9A"),
        ["Williams Racing"] = new("WIL", "williams", "#124A9A"),
        ["Cadillac"] = new("CAD", "cadillac", "#767679"),
        ["Haas F1 Team"] = new("HAS", "haas", "#9BA2A4"),
        ["MoneyGram Haas F1 Team"] = new("HAS", "haas", "#9BA2A4"),
        ["Aston Martin"] = new("AMR", "astonmartin", "#18684D"),
        ["Aston Martin Aramco Formula One Team"] = new("AMR", "astonmartin", "#18684D"),
        ["Alpine"] = new("ALP", "alpine", "#007CB2"),
        ["BWT Alpine F1 Team"] = new("ALP", "alpine", "#007CB2")
    };

    public static IReadOnlyList<DriverDefinition> CreateOfficial2026Grid()
        => new List<DriverDefinition>
        {
            new("RUS", "George Russell", "Mercedes", "MER", TeamLogo("mercedes"), "#27F4D2", 1.00),
            new("ANT", "Kimi Antonelli", "Mercedes", "MER", TeamLogo("mercedes"), "#27F4D2", 0.99),
            new("LEC", "Charles Leclerc", "Ferrari", "FER", TeamLogo("ferrari"), "#D5002A", 1.00),
            new("HAM", "Lewis Hamilton", "Ferrari", "FER", TeamLogo("ferrari"), "#D5002A", 0.99),
            new("NOR", "Lando Norris", "McLaren", "MCL", TeamLogo("mclaren"), "#ED7700", 1.00),
            new("PIA", "Oscar Piastri", "McLaren", "MCL", TeamLogo("mclaren"), "#ED7700", 1.00),
            new("VER", "Max Verstappen", "Red Bull Racing", "RBR", TeamLogo("redbullracing"), "#21477C", 1.01),
            new("HAD", "Isack Hadjar", "Red Bull Racing", "RBR", TeamLogo("redbullracing"), "#21477C", 0.97),
            new("GAS", "Pierre Gasly", "Alpine", "ALP", TeamLogo("alpine"), "#007CB2", 0.95),
            new("COL", "Franco Colapinto", "Alpine", "ALP", TeamLogo("alpine"), "#007CB2", 0.94),
            new("OCO", "Esteban Ocon", "Haas F1 Team", "HAS", TeamLogo("haas"), "#9BA2A4", 0.93),
            new("BEA", "Oliver Bearman", "Haas F1 Team", "HAS", TeamLogo("haas"), "#9BA2A4", 0.92),
            new("LAW", "Liam Lawson", "Racing Bulls", "RB", TeamLogo("racingbulls"), "#4070E8", 0.93),
            new("LIN", "Arvid Lindblad", "Racing Bulls", "RB", TeamLogo("racingbulls"), "#4070E8", 0.92),
            new("HUL", "Nico Hulkenberg", "Audi", "AUD", TeamLogo("audi"), "#CC2400", 0.94),
            new("BOR", "Gabriel Bortoleto", "Audi", "AUD", TeamLogo("audi"), "#CC2400", 0.92),
            new("SAI", "Carlos Sainz", "Williams", "WIL", TeamLogo("williams"), "#124A9A", 0.93),
            new("ALB", "Alexander Albon", "Williams", "WIL", TeamLogo("williams"), "#124A9A", 0.92),
            new("PER", "Sergio Perez", "Cadillac", "CAD", TeamLogo("cadillac"), "#767679", 0.91),
            new("BOT", "Valtteri Bottas", "Cadillac", "CAD", TeamLogo("cadillac"), "#767679", 0.90),
            new("ALO", "Fernando Alonso", "Aston Martin", "AMR", TeamLogo("astonmartin"), "#18684D", 0.94),
            new("STR", "Lance Stroll", "Aston Martin", "AMR", TeamLogo("astonmartin"), "#18684D", 0.91),
        };

    public static IReadOnlyList<DriverDefinition> CreateLiveGrid(IReadOnlyList<OpenF1DriverDto> liveDrivers)
        => liveDrivers
            .OrderBy(driver => driver.DriverNumber)
            .Select(CreateLiveDriver)
            .ToList();

    public static DriverDefinition CreateLiveDriver(OpenF1DriverDto liveDriver)
    {
        var style = ResolveTeamStyle(liveDriver.TeamName, liveDriver.TeamColour);
        var code = string.IsNullOrWhiteSpace(liveDriver.NameAcronym)
            ? BuildFallbackCode(liveDriver.FullName)
            : liveDriver.NameAcronym.Trim().ToUpperInvariant();

        return new DriverDefinition(
            code,
            ToTitleCaseName(liveDriver.FullName),
            string.IsNullOrWhiteSpace(liveDriver.TeamName) ? "Unknown Team" : liveDriver.TeamName.Trim(),
            style.Badge,
            TeamLogo(style.LogoFile),
            style.AccentHex,
            1.0,
            liveDriver.DriverNumber);
    }

    private static TeamStyle ResolveTeamStyle(string? teamName, string? teamColour)
    {
        if (!string.IsNullOrWhiteSpace(teamName))
        {
            foreach (var pair in TeamStyles)
            {
                if (teamName.Contains(pair.Key, StringComparison.OrdinalIgnoreCase) || pair.Key.Contains(teamName, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value with { AccentHex = NormalizeHex(teamColour, pair.Value.AccentHex) };
                }
            }
        }

        return new TeamStyle("F1", "haas", NormalizeHex(teamColour, "#E10600"));
    }

    private static string NormalizeHex(string? candidate, string fallback)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return fallback;
        }

        var trimmed = candidate.Trim();
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            return trimmed;
        }

        return trimmed.Length == 6 ? $"#{trimmed}" : fallback;
    }

    private static string ToTitleCaseName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return "Unknown Driver";
        }

        return string.Join(' ', fullName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
    }

    private static string BuildFallbackCode(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "DRV";
        }

        var source = parts[^1];
        return source.Length >= 3
            ? source[..3].ToUpperInvariant()
            : source.ToUpperInvariant().PadRight(3, 'X');
    }

    private static string TeamLogo(string fileName)
        => $"pack://application:,,,/Assets/Teams/{fileName}.png";

    private sealed record TeamStyle(string Badge, string LogoFile, string AccentHex);
}
