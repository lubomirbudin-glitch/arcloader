namespace Offload.Core.Desktop;

/// Where the app keeps its files (settings.json, hwtest.json, app.log): %APPDATA%\Offload, or the folder in
/// OFFLOAD_SETTINGS_DIR (isolated test and automation runs).
public static class AppData
{
    public static string Folder =>
        Environment.GetEnvironmentVariable("OFFLOAD_SETTINGS_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Offload");
}
