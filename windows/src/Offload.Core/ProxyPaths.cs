namespace Offload.Core;

/// How a card folder's `_PROXY` is laid out: Mirror (Windows from 0.5.1) = the clip's folder on the card is kept,
/// `_PROXY/idle/1.mov`; Flat (Windows 0.5.0 and older, the Mac) = every proxy at the top, `_PROXY/1.mov`, `1_2.mov` ….
public enum ProxyTree { Mirror, Flat }

/// Where a clip's proxy goes (relative to the card folder, with `/`). Cards often repeat file names in different folders
/// (idle\1.mp4, listening\1.mp4): mirrored, each keeps its name, so the editor relinks it by name. A name clash is then
/// possible only inside one folder (A.mov + A.mxf) — the second gets "_2" and Msg.ProxyRenamed, as before.
public static class ProxyPaths
{
    /// The clip's folder on the card ("" at the root) — the folder of its primary file; for an .ari sequence (whose Path
    /// is the sequence folder) its parent, so the proxy sits beside the sequence and is named after it.
    public static string Folder(Clip clip)
    {
        var path = clip.Path.TrimEnd('/');
        int slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }

    /// `_PROXY/<folder>/<name>.mov` (`_PROXY/<name>.mov` for "").
    public static string Relative(string folder, string name) =>
        Layout.ProxyDirName + "/" + (folder.Length > 0 ? folder + "/" : "") + name + ".mov";

    /// The key of a proxy in `used`: its path below `_PROXY` without `.mov` ("idle/1"; flat "1").
    static string Key(string folder, string name) => folder.Length > 0 ? folder + "/" + name : name;

    /// The proxy of `clip` in this layout: the first free name (`<name>`, `<name>_2` …) in its folder, taken into `used`
    /// (keys compared regardless of case, as NTFS and exFAT do). Returns the relative path and the name chosen.
    public static (string Path, string Name) Pick(Clip clip, ProxyTree tree, HashSet<string> used)
    {
        var folder = tree == ProxyTree.Mirror ? Folder(clip) : "";
        var name = clip.Name;
        for (int n = 2; !used.Add(Key(folder, name)); n++) name = $"{clip.Name}_{n}";
        return (Relative(folder, name), name);
    }

    /// The layout of an earlier backup from its clips (every generation's report): Flat when a clip from a subfolder of
    /// the card has its proxy at the top of `_PROXY` and none has one in its folder; otherwise (also no proxies at all,
    /// or clips only at the card's root, where both layouts agree) Mirror. An append keeps the backup's layout — the
    /// proxies already there are never moved.
    public static ProxyTree Detect(IEnumerable<ClipEntry> earlier)
    {
        bool flat = false;
        foreach (var e in earlier)
        {
            if (e.ProxyPath is not string p || Folder(e.Clip).Length == 0) continue;
            var below = p.StartsWith(Layout.ProxyDirName + "/", StringComparison.OrdinalIgnoreCase) ? p[(Layout.ProxyDirName.Length + 1)..] : p;
            if (below.Contains('/')) return ProxyTree.Mirror;
            flat = true;
        }
        return flat ? ProxyTree.Flat : ProxyTree.Mirror;
    }

    /// The layout of the backup in these card folders (their reports; touches the disk).
    public static ProxyTree Of(IReadOnlyList<string> cardPaths) =>
        Detect(cardPaths.SelectMany(p => CardRecord.Load(p).Clips.Select(c => c.Entry)));

    /// Every proxy already in `_PROXY` of these card folders, as Pick's keys ("idle/1", "C0001"); temporary `.part`
    /// files and folders that cannot be read are left out.
    public static IEnumerable<string> Existing(IReadOnlyList<string> cardPaths)
    {
        var keys = new List<string>();
        foreach (var p in cardPaths)
        {
            var dir = Path.Combine(p, Layout.ProxyDirName);
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    if (!string.Equals(Path.GetExtension(f), ".mov", StringComparison.OrdinalIgnoreCase)) continue;
                    var rel = Path.GetRelativePath(dir, f).Replace('\\', '/');
                    keys.Add(rel[..^".mov".Length]);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A destination that went away: nothing is rendered or copied there now either.
            }
        }
        return keys;
    }
}
