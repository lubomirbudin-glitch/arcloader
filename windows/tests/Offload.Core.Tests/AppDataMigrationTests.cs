using System.Security.Cryptography;
using Offload.Core.Desktop;

namespace Offload.Core.Tests;

/// The rename (0.10.0): %APPDATA%\Offload is COPIED to %APPDATA%\ArcLoader once — only when the new folder does not
/// exist yet; the old one is never moved, changed or deleted (going back to an older version must still work). Every
/// test works in a temporary "roaming" folder, never the real %APPDATA%.
public class AppDataMigrationTests
{
    /// The old folder as a version before 0.10.0 left it: settings, history with its file lists, own LUTs, the log.
    static string OldFolder(TempDir roaming)
    {
        var old = roaming.Sub("Offload");
        File.WriteAllText(Path.Combine(old, "settings.json"), """{"theme":"dark","projects":[{"name":"Film X"}]}""");
        File.WriteAllText(Path.Combine(old, "history.json"), """{"entries":[]}""");
        File.WriteAllText(Path.Combine(old, "update-check.json"), "{}");
        File.WriteAllText(Path.Combine(old, "app.log"), "2026-10-10 10:00:00  something\n");
        TestFs.WriteRandom(Path.Combine(old, "history", "abc.files.gz"), 3000, 1);
        File.WriteAllText(Path.Combine(roaming.Sub("Offload", "luts"), "red-log3g10-rwg.cube"), "LUT_3D_SIZE 2\n");
        return old;
    }

    /// Every file under `folder`: relative path → SHA-256 and last write time.
    static SortedDictionary<string, (string Hash, DateTime Written)> Snapshot(string folder) =>
        new(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(
            f => Path.GetRelativePath(folder, f),
            f => (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))), File.GetLastWriteTimeUtc(f))), StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void OldFolderOnly_IsCopied_OldOneUntouched()
    {
        using var roaming = new TempDir();
        var old = OldFolder(roaming);
        var before = Snapshot(old);
        var lines = new List<string>();

        Assert.True(AppData.MigrateFromOffload(null, null, roaming.Path, lines.Add));

        var arc = Path.Combine(roaming.Path, "ArcLoader");
        Assert.Equal(before.ToDictionary(e => e.Key, e => e.Value.Hash), Snapshot(arc).ToDictionary(e => e.Key, e => e.Value.Hash));
        Assert.Equal(before, Snapshot(old));   // the old folder: the same files, contents and times
        Assert.True(Directory.Exists(Path.Combine(arc, "luts")));
        var line = Assert.Single(lines);
        Assert.Contains(old, line);
        Assert.Contains(arc, line);
        // Nothing left over next to it (the copy is made in a temporary folder and renamed).
        Assert.Equal(["ArcLoader", "Offload"], Directory.GetDirectories(roaming.Path).Select(Path.GetFileName).Order());

        // Once only: the next start finds the new folder and copies nothing (even when the old one changed meanwhile).
        File.WriteAllText(Path.Combine(old, "settings.json"), "{}");
        Assert.False(AppData.MigrateFromOffload(null, null, roaming.Path, lines.Add));
        Assert.Single(lines);
        Assert.Equal("""{"theme":"dark","projects":[{"name":"Film X"}]}""", File.ReadAllText(Path.Combine(arc, "settings.json")));
    }

    [Fact]
    public void BothExist_NothingCopied()
    {
        using var roaming = new TempDir();
        var old = OldFolder(roaming);
        var arc = roaming.Sub("ArcLoader");
        File.WriteAllText(Path.Combine(arc, "settings.json"), """{"theme":"light"}""");
        var oldBefore = Snapshot(old);
        var arcBefore = Snapshot(arc);
        var lines = new List<string>();

        Assert.False(AppData.MigrateFromOffload(null, null, roaming.Path, lines.Add));

        Assert.Equal(arcBefore, Snapshot(arc));
        Assert.Equal(oldBefore, Snapshot(old));
        Assert.Empty(lines);
    }

    [Fact]
    public void EmptyNewFolder_CountsAsExisting()
    {
        // A new folder that exists at all (even empty) is the user's: never filled from the old one.
        using var roaming = new TempDir();
        OldFolder(roaming);
        var arc = roaming.Sub("ArcLoader");
        Assert.False(AppData.CopyOldFolder(Path.Combine(roaming.Path, "Offload"), arc, null));
        Assert.Empty(Directory.EnumerateFileSystemEntries(arc));
    }

    [Fact]
    public void NoOldFolder_NothingHappens()
    {
        using var roaming = new TempDir();
        var lines = new List<string>();
        Assert.False(AppData.MigrateFromOffload(null, null, roaming.Path, lines.Add));
        Assert.False(Directory.Exists(Path.Combine(roaming.Path, "ArcLoader")));
        Assert.Empty(lines);
    }

    [Fact]
    public void DemoOrSettingsVariable_NeverMigrates()
    {
        using var roaming = new TempDir();
        OldFolder(roaming);
        Assert.False(AppData.MigrateFromOffload(@"D:\iso", null, roaming.Path, null));
        Assert.False(AppData.MigrateFromOffload(null, Path.Combine(roaming.Path, "demo"), roaming.Path, null));
        Assert.False(Directory.Exists(Path.Combine(roaming.Path, "ArcLoader")));
        Assert.True(AppData.MigrateFromOffload("", "", roaming.Path, null));   // empty = not set
    }

    [Fact]
    public void FileInUse_IsLeftOut_RestCopied()
    {
        using var roaming = new TempDir();
        var old = OldFolder(roaming);
        var lines = new List<string>();
        // Held open without sharing (another program writing it): it cannot be read.
        using (new FileStream(Path.Combine(old, "app.log"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.True(AppData.MigrateFromOffload(null, null, roaming.Path, lines.Add));
        var arc = Path.Combine(roaming.Path, "ArcLoader");
        Assert.True(File.Exists(Path.Combine(arc, "settings.json")));
        Assert.True(File.Exists(Path.Combine(arc, "history", "abc.files.gz")));
        Assert.False(File.Exists(Path.Combine(arc, "app.log")));
        Assert.Contains("app.log", Assert.Single(lines));
        Assert.True(File.Exists(Path.Combine(old, "app.log")));
    }

    [Fact]
    public void RealFolder_IsArcLoader_OldNameOffload()
    {
        Assert.Equal("ArcLoader", AppData.FolderName);
        Assert.Equal("Offload", AppData.OldFolderName);
        Assert.Equal(["ARCLOADER_SETTINGS_DIR", "OFFLOAD_SETTINGS_DIR"], AppData.SettingsDirVariables);
    }
}
