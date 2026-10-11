namespace Offload.Core.Desktop;

/// Where the app keeps its files (settings.json, history.json, hwtest.json, app.log, luts\, update-check.json):
/// %APPDATA%\mmMover, or the folder in MMMOVER_SETTINGS_DIR / ARCLOADER_SETTINGS_DIR / OFFLOAD_SETTINGS_DIR (isolated test
/// and automation runs). `--demo` (ruling I3) uses a folder of its own in %TEMP% — whatever those variables say — so the
/// demo can never touch the real settings, history or log. Earlier versions used %APPDATA%\ArcLoader (0.10.x) and
/// %APPDATA%\Offload (up to 0.9.x): the first start copies the newest of them (MigrateFromOlder), never moves or deletes
/// it — an older version still finds its own files.
public static class AppData
{
    public const string FolderName = Product.Name;

    /// The folders of earlier versions, newest first: "ArcLoader" (0.10.x), "Offload" (up to 0.9.x). The first one that
    /// exists is copied.
    public static readonly IReadOnlyList<string> OldFolderNames = ["ArcLoader", "Offload"];

    /// The settings-folder variables, the first one set wins: the new name, then the older ones (tests, automation).
    public static readonly IReadOnlyList<string> SettingsDirVariables = ["MMMOVER_SETTINGS_DIR", "ARCLOADER_SETTINGS_DIR", "OFFLOAD_SETTINGS_DIR"];

    static volatile string? demoFolder;

    public static string Folder => Resolve(SettingsDirVariable(), demoFolder,
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    /// The first settings-folder variable that is set (non-empty), or null.
    static string? SettingsDirVariable() =>
        SettingsDirVariables.Select(Environment.GetEnvironmentVariable).FirstOrDefault(v => v is { Length: > 0 });

    /// The demo's folder wins over everything; then MMMOVER_ / ARCLOADER_ / OFFLOAD_SETTINGS_DIR; then %APPDATA%\mmMover.
    public static string Resolve(string? settingsDirVariable, string? demo, string roamingAppData) =>
        demo is { Length: > 0 } ? demo
        : settingsDirVariable is { Length: > 0 } dir ? dir
        : Path.Combine(roamingAppData, FolderName);

    /// `--demo`: from here on every file of the app (settings, history, log, update state, hardware test) lives in a new
    /// folder under %TEMP%. Called once, before anything reads or writes. Returns the folder.
    public static string UseDemoFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), Product.Name + "-demo-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        demoFolder = folder;
        return folder;
    }

    /// The renames (0.10.0 Offload → ArcLoader, 0.11.0 ArcLoader → mmMover): at the start of the GUI and the CLI, before
    /// anything reads or writes a file of the app. Only for the real folder — never with `--demo` or a settings-folder
    /// variable. See CopyOldFolder. `log` gets one line when something was copied (or failed).
    public static void MigrateFromOlder(Action<string>? log) =>
        MigrateFromOlder(SettingsDirVariable(), demoFolder, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), log);

    /// MigrateFromOlder with what it reads passed in (tests: a temporary "roaming" folder). When %APPDATA%\mmMover does
    /// not exist, the first of OldFolderNames that does (ArcLoader, else Offload) is copied to it. True when copied.
    internal static bool MigrateFromOlder(string? settingsDirVariable, string? demo, string roamingAppData, Action<string>? log)
    {
        if (demo is { Length: > 0 } || settingsDirVariable is { Length: > 0 } || roamingAppData.Length == 0) return false;
        var newFolder = Path.Combine(roamingAppData, FolderName);
        var source = OldFolderNames.Select(n => Path.Combine(roamingAppData, n)).FirstOrDefault(Exists);
        return source != null && CopyOldFolder(source, newFolder, log);
    }

    static bool Exists(string folder)
    {
        try { return Directory.Exists(folder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    /// Copies `oldFolder` (with every subfolder) to `newFolder` when `newFolder` does not exist and `oldFolder` does;
    /// otherwise nothing happens. The old folder is only read — never moved, changed or deleted, so going back to an
    /// older version still works. The copy is made in a temporary folder next to the new one and renamed at the end: a
    /// copy cut short (a crash, a power cut) never looks finished, the next start tries again. A file that cannot be read
    /// (in use) is left out and named in the log line; the rest is copied. True when the new folder was made here.
    public static bool CopyOldFolder(string oldFolder, string newFolder, Action<string>? log)
    {
        try
        {
            if (Directory.Exists(newFolder) || !Directory.Exists(oldFolder)) return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(newFolder)) ?? ".";
        var temp = Path.Combine(parent, Path.GetFileName(Path.TrimEndingDirectorySeparator(newFolder)) + ".copying-" + Guid.NewGuid().ToString("N")[..8]);
        var skipped = new List<string>();
        try
        {
            CopyTree(oldFolder, temp, oldFolder, skipped);
            try
            {
                Directory.Move(temp, newFolder);
            }
            catch (IOException) when (Directory.Exists(newFolder))
            {
                // The GUI and the CLI started at the same moment: the other one copied it already.
                TryDelete(temp);
                return false;
            }
            log?.Invoke($"settings copied from {oldFolder} to {newFolder} (the old folder is left as it was)"
                + (skipped.Count > 0 ? $"; not copied (could not be read): {string.Join(", ", skipped)}" : ""));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            log?.Invoke($"could not copy the settings from {oldFolder} to {newFolder}: {e.Message}");
            return false;
        }
    }

    static void CopyTree(string from, string to, string root, List<string> skipped)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            try
            {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                skipped.Add(Path.GetRelativePath(root, file));
            }
        }
        foreach (var dir in Directory.EnumerateDirectories(from))
        {
            // A reparse point (junction, symbolic link) is not followed: it could lead anywhere, even back here.
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) { skipped.Add(Path.GetRelativePath(root, dir)); continue; }
            CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)), root, skipped);
        }
    }

    static void TryDelete(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
