using System.Globalization;
using System.Text.RegularExpressions;
using Offload.Core.History;

namespace Offload.Core;

/// The reel ("A001") in a camera's clip name (spec 2.3) — from the name only, clips are never read before the copy (R10).
public static partial class ReelNames
{
    [GeneratedRegex(@"^(?<cam>[A-Z])(?<reel>\d{3})_\d{8}_C(?<clip>\d{3})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Blackmagic();

    [GeneratedRegex(@"^(?<cam>[A-Z])(?<reel>\d{3})_C(?<clip>\d{3})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Red();

    /// ARRI, Sony Venice, Canon Cinema EOS.
    [GeneratedRegex(@"^(?<cam>[A-Z])(?<reel>\d{3})C(?<clip>\d{3,4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArriSonyCanon();

    /// The reel in upper case and the part of the name that gave it ("A001C003"), or null (Sony XAVC "C0001", Panasonic,
    /// DJI, phones …). `name`: the clip name without extension.
    public static (string Reel, string Prefix)? Match(string name)
    {
        foreach (var r in new[] { Blackmagic(), Red(), ArriSonyCanon() })
            if (r.Match(name) is { Success: true } m)
                return ((m.Groups["cam"].Value + m.Groups["reel"].Value).ToUpperInvariant(), m.Value);
        return null;
    }

    public static string? FromClipName(string name) => Match(name)?.Reel;
}

/// A card folder name proposed instead of the volume label (spec 2.3). `Hint` is the grey line under the name field.
public sealed record NameSuggestion(string Name, string Hint);

public static partial class ReelSuggestion
{
    [GeneratedRegex(@"^[A-Z]\d{3}\z")]
    private static partial Regex ReelFolder();

    /// One reel among all recognised clips (camera proxies left out; an ARRIRAW sequence by its folder name, which is its
    /// clip name) → that reel. Several reels → no suggestion, but `Note` says so. `Reels`: every reel found, sorted.
    public static (NameSuggestion? Suggestion, string? Note, IReadOnlyList<string> Reels) FromClips(IReadOnlyList<Clip> clips)
    {
        var named = clips.Where(c => !c.IsCameraProxy)
            .Select(c => ReelNames.Match(c.Name))
            .Where(m => m != null)
            .Select(m => m!.Value)
            .ToList();
        var reels = named.Select(m => m.Reel).Distinct().Order(StringComparer.Ordinal).ToList();
        return reels.Count switch
        {
            0 => (null, null, reels),
            1 => (new NameSuggestion(reels[0], $"podle názvů klipů ({named[0].Prefix}…)"), null, reels),
            _ => (null, $"Na kartě je víc cívek: {string.Join(", ", reels)}", reels),
        };
    }

    /// "Next in line": history entries of the same date folder (and, from plan 8, project) named like "A001"; exactly
    /// one camera letter → the next number ("A001" → "A002"). Several letters → null (which camera is this card?).
    public static NameSuggestion? Next(IEnumerable<HistoryEntry> history, string date, string? project, string today)
    {
        var names = history.Where(e => e.Date == date && e.Project == project && ReelFolder().IsMatch(e.CardName))
            .Select(e => e.CardName).ToList();
        if (names.Count == 0 || names.Select(n => n[0]).Distinct().Count() != 1) return null;
        int max = names.Max(n => int.Parse(n[1..], NumberStyles.None, CultureInfo.InvariantCulture));
        if (max >= 999) return null;
        var letter = names[0][0];
        var when = date == today ? "dnes" : date;
        return new NameSuggestion($"{letter}{max + 1:000}", $"další v řadě po {letter}{max:000} ({when})");
    }
}
