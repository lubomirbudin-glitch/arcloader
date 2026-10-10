using Offload.Core.History;

namespace Offload.Core.Desktop;

/// What Start does with a selected card (spec 2.2): a full copy, an append, or nothing because the append the user
/// ticked is no longer possible (the card changed, was swapped or lost its history) — never a silent full copy instead.
public enum StartKind { Full, Append, AppendGone }

/// The ticks of "Append (new clips only)" in the Cards list. A tick belongs to the history entry the user saw
/// (`EntryId`): it counts only while that same entry still offers the append, and it never comes back by itself.
/// UI thread only; memory only.
public sealed class AppendTicks
{
    readonly Dictionary<string, (string EntryId, string CardName)> ticks = new(StringComparer.OrdinalIgnoreCase);
    // Ticks that were removed because the offer went away; Start tells the user once and does not copy that card.
    readonly Dictionary<string, string> gone = new(StringComparer.OrdinalIgnoreCase);

    /// The user ticks the box of the offer in `insight`; false (nothing set) when there is none.
    public bool Tick(string path, CardInsight? insight)
    {
        if (!AppendRules.Offer(insight)) return false;
        var e = insight!.History!.Entry;
        ticks[path] = (e.Id, e.CardName);
        gone.Remove(path);
        return true;
    }

    public void Untick(string path)
    {
        ticks.Remove(path);
        gone.Remove(path);
    }

    /// The card left the list.
    public void Drop(string path) => Untick(path);

    /// Ticked, and the same entry still offers the append.
    public bool Chosen(string path, CardInsight? insight) =>
        ticks.TryGetValue(path, out var t) && AppendRules.Offer(insight) && insight!.History!.Entry.Id == t.EntryId;

    /// Ticked, or ticked until the offer went away (Start must not turn such a card into a full copy).
    public bool Wants(string path) => ticks.ContainsKey(path) || gone.ContainsKey(path);

    /// Removes every tick whose offer is gone (other entry, Changed, error, no insight yet). Returns the removed paths.
    public List<string> Prune(Func<string, CardInsight?> insight) =>
        ticks.Keys.ToList().Where(p => PruneOne(p, insight(p))).ToList();

    bool PruneOne(string path, CardInsight? insight)
    {
        if (!ticks.TryGetValue(path, out var t) || Chosen(path, insight)) return false;
        ticks.Remove(path);
        gone[path] = t.CardName;
        return true;
    }

    /// Decides for Start, after a fresh prune. An `AppendGone` card is forgotten (its tick is already off), so the next
    /// Start treats it as an ordinary card.
    public (StartKind Kind, string? CardName) Classify(string path, CardInsight? insight)
    {
        PruneOne(path, insight);
        if (gone.Remove(path, out var name)) return (StartKind.AppendGone, name);
        return Chosen(path, insight) ? (StartKind.Append, null) : (StartKind.Full, null);
    }

    /// "Card A001 has changed — it can no longer be appended. Check it and start again."
    public static string GoneMessage(IReadOnlyList<string> names) => names.Count == 1
        ? $"Card {names[0]} has changed — it can no longer be appended. Check it and start again."
        : $"Cards {string.Join(", ", names)} have changed — they can no longer be appended. Check them and start again.";
}
