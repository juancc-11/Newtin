namespace Newtin.Infrastructure.FileSystem;

/// <summary>Los datos del usuario viven en %LOCALAPPDATA%\Newtin, nunca junto al ejecutable.</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Newtin");

    public static string Logs => Path.Combine(Root, "logs");
    public static string Reports => Path.Combine(Root, "reports");
    public static string Whiteboard => Path.Combine(Root, "whiteboard");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string LocalTests => Path.Combine(Root, "permission-tests");
}