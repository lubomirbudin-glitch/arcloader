using System.Globalization;

namespace Offload.Core;

/// Destination layout: <root>/<YYYY-MM-DD>/<card name>/
public static class Layout
{
    public const string ReportDirName = "_REPORT";
    public const string ProxyDirName = "_PROXY";

    const string InvalidChars = "<>:\"/\\|?*";

    static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string Today() => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// Joins a root folder with a report-style relative path ("A/B/c.mov").
    public static string Join(string root, string rel) => Path.Combine(root, rel.Replace('/', '\\'));

    public static string CardPath(string root, string date, string cardName) => Path.Combine(root, date, cardName);

    /// DefaultCardName without touching the disk: `label` is the volume label the scan already read (used only when
    /// the source is a drive root). For the GUI, whose UI thread never waits on a disk.
    public static string DefaultCardName(string source, string? label)
    {
        if (source.Length == 2 && source[1] == ':') source += "\\";   // "F:" alone means the current folder on F:
        var full = Path.GetFullPath(source);
        var root = Path.GetPathRoot(full);
        if (root != null && string.Equals(full.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(label))
            return Sanitize(label);
        return Sanitize(Path.GetFileName(full.TrimEnd('\\')));
    }

    /// Volume label when the source is a drive root, otherwise the folder name.
    public static string DefaultCardName(string source)
    {
        var full = Path.GetFullPath(source);
        var root = Path.GetPathRoot(full);
        if (root != null && string.Equals(full.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            var label = NativeIO.VolumeLabel(root);
            if (!string.IsNullOrWhiteSpace(label)) return Sanitize(label);
        }
        return Sanitize(Path.GetFileName(full.TrimEnd('\\')));
    }

    /// A name Windows can use as a folder: forbidden characters become "_", no trailing dot or space, no device names.
    public static string Sanitize(string s)
    {
        var chars = s.Select(c => c < 0x20 || InvalidChars.Contains(c) ? '_' : c).ToArray();
        var o = new string(chars).Trim().TrimEnd('.', ' ');
        if (o.Length == 0) return "CARD";
        return ReservedNames.Contains(BaseName(o)) ? "_" + o : o;
    }

    /// Why Windows cannot store a file or folder with this name, or null when it can.
    public static string? InvalidNameReason(string name)
    {
        if (name.Any(c => c < 0x20 || InvalidChars.Contains(c))) return "contains a character Windows does not allow";
        if (name.EndsWith('.') || name.EndsWith(' ')) return "ends with a dot or space";
        if (ReservedNames.Contains(BaseName(name))) return "reserved device name on Windows";
        return null;
    }

    static string BaseName(string name)
    {
        int dot = name.IndexOf('.');
        return (dot < 0 ? name : name[..dot]).TrimEnd(' ');
    }

    /// Card folder name that is free on every destination (NAME, NAME_2, NAME_3 …),
    /// so a re-inserted card never overwrites an earlier offload.
    public static string UniqueCardFolder(IEnumerable<string> roots, string date, string name)
    {
        var list = roots.ToList();
        var candidate = name;
        for (int n = 2; list.Any(r => Path.Exists(CardPath(r, date, candidate))); n++) candidate = $"{name}_{n}";
        return candidate;
    }
}
