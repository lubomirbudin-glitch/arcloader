using System.Runtime.CompilerServices;
using Offload.Core.History;

namespace Offload.Core.Tests;

static class TestEnvironment
{
    internal static string HistoryRoot => Path.Combine(Path.GetTempPath(), "offload-tests");

    /// Every OffloadJob a test runs records history (R27). Without this it would land in the developer's own
    /// %APPDATA%\ArcLoader\history.json — set once, before any test, independent of OFFLOAD_SETTINGS_DIR (which
    /// SettingsStoreTests.AppDataFolder_Override changes for a moment). Stale folders of earlier runs are removed (M8).
#pragma warning disable CA2255 // a test assembly: the initializer only isolates the tests' own app data
    [ModuleInitializer]
    internal static void IsolateHistory()
    {
        try
        {
            if (Directory.Exists(HistoryRoot))
                foreach (var pattern in new[] { "history-*", "appdata-*" })
                    foreach (var old in Directory.EnumerateDirectories(HistoryRoot, pattern))
                        try { Directory.Delete(old, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        HistoryStore.DefaultFolderOverride = Path.Combine(HistoryRoot, "history-" + Environment.ProcessId);
        // Everything else the app keeps in %APPDATA%\ArcLoader (own LUTs in luts\, settings, update state) goes to a temp
        // folder too, through the app's own seam — the developer's own files never change a test (or the other way round).
        var appData = Path.Combine(HistoryRoot, "appdata-" + Environment.ProcessId);
        Directory.CreateDirectory(appData);
        // Both names (0.10.0: ARCLOADER_SETTINGS_DIR wins, OFFLOAD_SETTINGS_DIR still works): a shell with either set
        // never leads a test to the developer's own folder.
        Environment.SetEnvironmentVariable("ARCLOADER_SETTINGS_DIR", appData);
        Environment.SetEnvironmentVariable("OFFLOAD_SETTINGS_DIR", appData);
        // Task 2026-10-09: the local report copies of CLI and settings-made jobs go to %TEMP%, never the real Documents.
        Offload.Core.Outputs.LocalReports.DocumentsOverride = Path.Combine(HistoryRoot, "history-" + Environment.ProcessId, "Documents");
    }
#pragma warning restore CA2255
}

public class TestEnvironmentTests
{
    [Fact]
    public void HistoryRedirect_IsActive_NeverTheRealAppData()
    {
        var folder = HistoryStore.Default().Folder;
        Assert.Equal(Path.Combine(TestEnvironment.HistoryRoot, "history-" + Environment.ProcessId), folder);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.False(folder.StartsWith(appData, StringComparison.OrdinalIgnoreCase));
    }

}
