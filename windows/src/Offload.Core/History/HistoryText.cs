using System.Globalization;
using Offload.Core.Desktop;

namespace Offload.Core.History;

public enum HistoryTone { Neutral, Ok, Warning, Error }

/// The history line under a card (spec 2.1 table) and in the CLI.
public static class HistoryText
{
    public const string Inspecting = "Zjišťuji, jestli už je karta zálohovaná…";

    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");

    /// "6. 10. 2026 8:15" in local time.
    public static string When(DateTime finishedUtc) => finishedUtc.ToLocalTime().ToString("d. M. yyyy H:mm", Cs);

    /// "Zaloha (D:)": the destination folder's name and its drive letter; a drive root alone is "D:", a share folder
    /// only its name.
    public static string DiskName(string root)
    {
        var trimmed = root.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        if (root.StartsWith(@"\\", StringComparison.Ordinal)) return name.Length > 0 ? name : root;
        var letter = trimmed.Length >= 2 && trimmed[1] == ':' ? trimmed[..2].ToUpperInvariant() : null;
        if (name.Length == 0 || trimmed.Length <= 2) return letter ?? root;
        return letter != null ? $"{name} ({letter})" : name;
    }

    static string Files(int n) => $"{n} {Texts.Plural(n, "soubor", "soubory", "souborů")}";

    /// `fullPaths`: the CLI names the destinations by their full root ("D:\Zaloha"), the GUI by DiskName.
    public static (string Text, HistoryTone Tone) Line(HistoryMatch m, bool fullPaths = false)
    {
        var e = m.Entry;
        var disks = string.Join(", ", e.Destinations.Select(d => fullPaths ? d.Root : DiskName(d.Root)));
        var head = $"Zálohováno {When(e.Finished)} na {disks}";
        if (m.Kind == HistoryMatchKind.Changed)
            return ($"{head}, ale karta se od té doby změnila ({Files(m.Changed.Count)} jinak) — doplnit nejde, jen celá kopie", HistoryTone.Warning);
        var (state, tone) = !e.Success ? ($" — s chybami ({Files(e.FailedFiles ?? 0)})", HistoryTone.Error)
            : !e.Verified ? (" — NEOVĚŘENO", HistoryTone.Warning)
            : (" — ověřeno", HistoryTone.Ok);
        var text = head + state;
        if (m.Kind == HistoryMatchKind.NewFiles)
        {
            text += $" · nové soubory: {m.New.Count} ({Format.BytesCs(m.NewBytes)})";
            if (tone == HistoryTone.Ok) tone = HistoryTone.Warning;
        }
        return (text, tone);
    }
}
