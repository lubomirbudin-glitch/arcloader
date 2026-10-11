using System.Security.Cryptography;
using Offload.Core.Desktop;

namespace Offload.Core.Tests;

/// The renames (0.10.0 Offload → ArcLoader, 0.11.0 ArcLoader → mmMover): the newest folder of an earlier version —
/// %APPDATA%\ArcLoader, else %APPDATA%\Offload — is COPIED to %APPDATA%\mmMover once, only when the new folder does not
/// exist yet; the old ones are never moved, changed or deleted (going back to an older version must still work). Every
/// test works in a temporary "roaming" folder, never the real %APPDATA%.
public class AppDataMigrationTests
{
    /// A folder as an earlier version left it: settings, history with its file lists, own LUTs, the log. `theme` tells
    /// the two old folders apart.
    static string OldFolder(TempDir roaming, string name = "Offload", string theme = "dark")
    {
        var old = roaming.Sub(name);
        File.WriteAllText(Path.Combine(old, "settings.json"), $$"""{"theme":"{{theme}}","projects":[{"name":"Film X"}]}""");
        File.WriteAllText(Path.Combine(old, "history.json"), """{"entries":[]}""");
        File.WriteAllText(Path.Combine(old, "update-check.json"), "{}");
        File.WriteAllText(Path.Combine(old, "app.log"), $"2026-10-10 10:00:00  {name}\n");
        TestFs.WriteRandom(Path.Combine(old, "history", "abc.files.gz"), 3000, name.Length);
        File.WriteAllText(Path.Combine(roaming.Sub(name, "luts"), "red-log3g10-rwg.cube"), "LUT_3D_SIZE 2\n");
        return old;
    }

    /// Every file under `folder`: relative path → SHA-256 and last write time.
    static SortedDictionary<string, (string Hash, DateTime Written)> Snapshot(string folder) =>
        new(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(
            f => Path.GetRelativePath(folder, f),
            f => (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))), File.GetLastWriteTimeUtc(f))), StringComparer.OrdinalIgnoreCase);

    static Dictionary<string, string> Hashes(string folder) => Snapshot(folder).ToDictionary(e => e.Key, e => e.Value.Hash);

    static string[] Folders(TempDir roaming) => Directory.GetDirectories(roaming.Path).Select(f => Path.GetFileName(f)!).Order().ToArray();

    [Fact]
    public void OnlyOffload_IsCopied_OldOneUntouched()
    {
        using var roaming = new TempDir();
        var old = OldFolder(roaming);
        var before = Snapshot(old);
        var lines = new List<string>();

        Assert.True(AppData.MigrateFromOlder(null, null, roaming.Path, lines.Add));

        var mm = Path.Combine(roaming.Path, "mmMover");
        Assert.Equal(Hashes(old), Hashes(mm));
        Assert.Equal(before, Snapshot(old));   // the old folder: the same files, contents and times
        Assert.True(Directory.Exists(Path.Combine(mm, "luts")));
        var line = Assert.Single(lines);
        Assert.Contains(old, line);
        Assert.Contains(mm, line);
        // Nothing left over next to it (the copy is made in a temporary folder and renamed), no ArcLoader folder made.
        Assert.Equal(["mmMover", "Offload"], Folders(roaming));

        // Once only: the next start finds the new folder and copies nothing (even when the old one changed meanwhile).
        File.WriteAllText(Path.Combine(old, "settings.json"), "{}");
        Assert.False(AppData.MigrateFromOlder(null, null, roaming.Path, lines.Add));
        Assert.Single(lines);
        Assert.Equal("""{"theme":"dark","projects":[{"name":"Film X"}]}""", File.ReadAllText(Path.Combine(mm, "settings.json")));
    }

    [Fact]
    public void OnlyArcLoader_IsCopied_OldOneUntouched()
    {
        using var roaming = new TempDir();
        var arc = OldFolder(roaming, "ArcLoader", "light");
        var before = Snapshot(arc);
        var lines = new List<string>();

        Assert.True(AppData.MigrateFromOlder(null, null, roaming.Path, lines.Add));

        var mm = Path.Combine(roaming.Path, "mmMover");
        Assert.Equal(Hashes(arc), Hashes(mm));
        Assert.Equal(before, Snapshot(arc));
        Assert.Contains(arc, Assert.Single(lines));
        Assert.Equal(["ArcLoader", "mmMover"], Folders(roaming));
        Assert.Contains("\"light\"", File.ReadAllText(Path.Combine(mm, "settings.json")));
    }

    [Fact]
    public void ArcLoaderAndOffload_ArcLoaderWins_BothUntouched()
    {
        // ArcLoader (0.10.x) is newer than Offload (≤ 0.9.x): a user who went Offload → ArcLoader has both, and the
        // ArcLoader one holds what they did since.
        using var roaming = new TempDir();
        var off = OldFolder(roaming, "Offload", "dark");
        var arc = OldFolder(roaming, "ArcLoader", "light");
        var offBefore = Snapshot(off);
        var arcBefore = Snapshot(arc);
        var lines = new List<string>();

        Assert.True(AppData.MigrateFromOlder(null, null, roaming.Path, lines.Add));

        var mm = Path.Combine(roaming.Path, "mmMover");
        Assert.Equal(Hashes(arc), Hashes(mm));
        Assert.NotEqual(Hashes(off), Hashes(mm));
        Assert.Contains("\"light\"", File.ReadAllText(Path.Combine(mm, "settings.json")));
        Assert.Contains("ArcLoader", File.ReadAllText(Path.Combine(mm, "app.log")));
        Assert.Equal(offBefore, Snapshot(off));
        Assert.Equal(arcBefore, Snapshot(arc));
        var line = Assert.Single(lines);
        Assert.Contains(arc, line);
        Assert.DoesNotContain(off + " ", line);
        Assert.Equal(["ArcLoader", "mmMover", "Offload"], Folders(roaming));
    }

    [Fact]
    public void MmMoverExists_NothingCopied_OldOnesUntouched()
    {
        using var roaming = new TempDir();
        var off = OldFolder(roaming, "Offload");
        var arc = OldFolder(roaming, "ArcLoader", "light");
        var mm = roaming.Sub("mmMover");
        File.WriteAllText(Path.Combine(mm, "settings.json"), """{"theme":"system"}""");
        var offBefore = Snapshot(off);
        var arcBefore = Snapshot(arc);
        var mmBefore = Snapshot(mm);
        var lines = new List<string>();

        Assert.False(AppData.MigrateFromOlder(null, null, roaming.Path, lines.Add));

        Assert.Equal(mmBefore, Snapshot(mm));
        Assert.Equal(offBefore, Snapshot(off));
        Assert.Equal(arcBefore, Snapshot(arc));
        Assert.Empty(lines);
        Assert.Equal(["ArcLoader", "mmMover", "Offload"], Folders(roaming));
    }

    [Fact]
    public void EmptyNewFolder_CountsAsExisting()
    {
        // A new folder that exists at all (even empty) is the user's: never filled from an old one.
        using var roaming = new TempDir();
        OldFolder(roaming, "ArcLoader");
        var mm = roaming.Sub("mmMover");
        Assert.False(AppData.MigrateFromOlder(null, null, roaming.Path, null));
        Assert.False(AppData.CopyOldFolder(Path.Combine(roaming.Path, "ArcLoader"), mm, null));
        Assert.Empty(Directory.EnumerateFileSystemEntries(mm));
    }

    [Fact]
    public void NoOldFolder_NothingHappens()
    {
        using var roaming = new TempDir();
        var lines = new List<string>();
        Assert.False(AppData.MigrateFromOlder(null, null, roaming.Path, lines.Add));
        Assert.False(Directory.Exists(Path.Combine(roaming.Path, "mmMover")));
        Assert.Empty(lines);
    }

    [Fact]
    public void DemoOrSettingsVariable_NeverMigrates()
    {
        using var roaming = new TempDir();
        OldFolder(roaming, "ArcLoader");
        Assert.False(AppData.MigrateFromOlder(@"D:\iso", null, roaming.Path, null));
        Assert.False(AppData.MigrateFromOlder(null, Path.Combine(roaming.Path, "demo"), roaming.Path, null));
        Assert.False(Directory.Exists(Path.Combine(roaming.Path, "mmMover")));
        Assert.True(AppData.MigrateFromOlder("", "", roaming.Path, null));   // empty = not set
    }

    [Fact]
    public void FileInUse_IsLeftOut_RestCopied()
    {
        using var roaming = new TempDir();
        var old = OldFolder(roaming, "ArcLoader");
        var lines = new List<string>();
        // Held open without sharing (another program writing it): it cannot be read.
        using (new FileStream(Path.Combine(old, "app.log"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.True(AppData.MigrateFromOlder(null, null, roaming.Path, lines.Add));
        var mm = Path.Combine(roaming.Path, "mmMover");
        Assert.True(File.Exists(Path.Combine(mm, "settings.json")));
        Assert.True(File.Exists(Path.Combine(mm, "history", "abc.files.gz")));
        Assert.False(File.Exists(Path.Combine(mm, "app.log")));
        Assert.Contains("app.log", Assert.Single(lines));
        Assert.True(File.Exists(Path.Combine(old, "app.log")));
    }

    [Fact]
    public void RealFolder_IsMmMover_OldNamesNewestFirst()
    {
        Assert.Equal("mmMover", AppData.FolderName);
        Assert.Equal(["ArcLoader", "Offload"], AppData.OldFolderNames);
        Assert.Equal(["MMMOVER_SETTINGS_DIR", "ARCLOADER_SETTINGS_DIR", "OFFLOAD_SETTINGS_DIR"], AppData.SettingsDirVariables);
    }
}
