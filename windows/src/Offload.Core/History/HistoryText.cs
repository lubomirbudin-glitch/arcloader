using System.Globalization;
using Offload.Core.Desktop;

namespace Offload.Core.History;

public enum HistoryTone { Neutral, Ok, Warning, Error }

/// The history line under a card (spec 2.1 table) and in the CLI.
public static class HistoryText
{
    public const string Inspecting = "Checking whether the card is already backed up…";

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// "2026-10-06 08:15" in local time.
    public static string When(DateTime finishedUtc) => finishedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", Inv);

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

    static string Files(int n) => Texts.N(n, "file");

    /// A card emptied in the camera after the backup (everything on it was backed up, then deleted).
    public static (string Text, HistoryTone Tone) Emptied(HistoryMatch m) =>
        ($"The card is empty — backed up earlier on {When(m.Entry.Finished)} to {string.Join(", ", m.Entry.Destinations.Select(d => DiskName(d.Root)))}", HistoryTone.Neutral);

    /// `fullPaths`: the CLI names the destinations by their full root ("D:\Zaloha"), the GUI by DiskName.
    public static (string Text, HistoryTone Tone) Line(HistoryMatch m, bool fullPaths = false)
    {
        var e = m.Entry;
        var disks = string.Join(", ", e.Destinations.Select(d => fullPaths ? d.Root : DiskName(d.Root)));
        // R6: a copy the app never finished — it can be resumed (Dokončit přerušenou zálohu).
        if (e.Unfinished) return ($"Unfinished backup on {When(e.Finished)} to {disks} — the copy was interrupted; it can be finished", HistoryTone.Warning);
        var head = $"Backed up on {When(e.Finished)} to {disks}";
        if (m.Kind == HistoryMatchKind.Changed)
            return ($"{head}, but the card has changed since ({Files(m.Changed.Count)} differ{(m.Changed.Count == 1 ? "s" : "")}) — cannot append, only a full copy", HistoryTone.Warning);
        var (state, tone) = !e.Success ? ($" — with errors ({Files(e.FailedFiles ?? 0)})", HistoryTone.Error)
            : !e.Verified ? (" — NOT VERIFIED (safe to eject, do not format)", HistoryTone.Warning)
            : (" — verified, safe to format", HistoryTone.Ok);
        var text = head + state;
        if (m.Kind == HistoryMatchKind.NewFiles)
        {
            text += $" · new files: {m.New.Count} ({Format.BytesShort(m.NewBytes)})";
            if (tone == HistoryTone.Ok) tone = HistoryTone.Warning;
        }
        return (text, tone);
    }
}
