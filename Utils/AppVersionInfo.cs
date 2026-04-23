namespace F1TrackMapper.Utils;

public static class AppVersionInfo
{
    public const string AppName = "F1 Track Mapper";
    public const string Channel = "Demo";
    public const string Version = "v40";

    public static string BuildLabel => $"{AppName} {Version} ({Channel})";
}
