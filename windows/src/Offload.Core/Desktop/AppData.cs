namespace Offload.Core.Desktop;

/// Where the app keeps its files (settings.json, hwtest.json, app.log): %APPDATA%\Offload, or the folder in
/// OFFLOAD_SETTINGS_DIR (isolated test and automation runs). `--demo` (ruling I3) uses a folder of its own in %TEMP% —
/// whatever OFFLOAD_SETTINGS_DIR says — so the demo can never touch the real settings, history or log.
public static class AppData
{
    static volatile string? demoFolder;

    public static string Folder => Resolve(Environment.GetEnvironmentVariable("OFFLOAD_SETTINGS_DIR"), demoFolder,
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    /// The demo's folder wins over everything; then OFFLOAD_SETTINGS_DIR; then %APPDATA%\Offload.
    public static string Resolve(string? settingsDirVariable, string? demo, string roamingAppData) =>
        demo is { Length: > 0 } ? demo
        : settingsDirVariable is { Length: > 0 } dir ? dir
        : Path.Combine(roamingAppData, "Offload");

    /// `--demo`: from here on every file of the app (settings, history, log, update state, hardware test) lives in a new
    /// folder under %TEMP%. Called once, before anything reads or writes. Returns the folder.
    public static string UseDemoFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Offload-demo-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        demoFolder = folder;
        return folder;
    }
}
