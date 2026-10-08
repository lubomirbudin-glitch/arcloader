using System.Runtime.CompilerServices;
using Offload.Core.History;

namespace Offload.Core.Tests;

static class TestEnvironment
{
    internal static string HistoryRoot => Path.Combine(Path.GetTempPath(), "offload-tests");

    /// Every OffloadJob a test runs records history (R27). Without this it would land in the developer's own
    /// %APPDATA%\Offload\history.json — set once, before any test, independent of OFFLOAD_SETTINGS_DIR (which
    /// SettingsStoreTests.AppDataFolder_Override changes for a moment). Stale folders of earlier runs are removed (M8).
#pragma warning disable CA2255 // a test assembly: the initializer only isolates the tests' own app data
    [ModuleInitializer]
    internal static void IsolateHistory()
    {
        try
        {
            if (Directory.Exists(HistoryRoot))
                foreach (var old in Directory.EnumerateDirectories(HistoryRoot, "history-*"))
                    try { Directory.Delete(old, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        HistoryStore.DefaultFolderOverride = Path.Combine(HistoryRoot, "history-" + Environment.ProcessId);
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
