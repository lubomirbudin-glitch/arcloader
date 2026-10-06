using Offload.Core;
using Offload.Core.History;

namespace Offload.App;

public enum SourceKind { Card, Drive, Folder }

/// One row in Karty: a volume or a manual folder; the tick and the card name live in MainViewModel.
/// `defaultName`: the placeholder — the scanned volume label or the folder name (no disk access on the UI thread).
public sealed class SourceItemViewModel(MainViewModel owner, string path, string title, string detail, SourceKind kind, string defaultName) : ObservableObject
{
    public string Path => path;
    public string Title => title;
    public string Detail => detail;
    public string Glyph => kind switch { SourceKind.Card => "\uE7F1", SourceKind.Drive => "\uEDA2", _ => "\uE8B7" };
    public string CardNamePlaceholder { get; } = defaultName;

    /// "Zálohováno … — ověřeno" (spec 2.1), or null for a card history does not know.
    public string? HistoryText => owner.HistoryLine(path).Text;
    public HistoryTone HistoryTone => owner.HistoryLine(path).Tone;

    /// A card inspection finished: the history line (and what depends on it) may have changed.
    internal void InsightChanged() => RaiseMany(nameof(HistoryText), nameof(HistoryTone));

    public bool IsSelected
    {
        get => owner.IsSelected(path);
        set { owner.SetSelected(path, value); Raise(); }
    }

    public string CardName
    {
        get => owner.CardName(path);
        set { owner.SetCardName(path, value); Raise(); }
    }
}
