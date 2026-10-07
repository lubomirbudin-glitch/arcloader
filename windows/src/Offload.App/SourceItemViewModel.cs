using Offload.Core;
using Offload.Core.History;

namespace Offload.App;

public enum SourceKind { Card, Drive, Folder }

/// One row in Karty: a volume or a manual folder; the tick and the card name live in MainViewModel.
public sealed class SourceItemViewModel(MainViewModel owner, string path, string title, string detail, SourceKind kind) : ObservableObject
{
    public string Path => path;
    public string Title => title;
    public string Detail => detail;
    public string Glyph => kind switch { SourceKind.Card => "\uE7F1", SourceKind.Drive => "\uEDA2", _ => "\uE8B7" };
    /// The name a start uses when none is typed: the clips' reel, the next in line, or the volume label / folder name.
    public string CardNamePlaceholder => owner.Placeholder(path).Name;
    /// "podle názvů klipů (A001C001…)", "další v řadě po A001 (dnes)", "Na kartě je víc cívek: …" — or null.
    public string? NameHint => owner.CardName(path).Trim().Length > 0 ? null : owner.Placeholder(path).Hint;

    /// " — podle názvů klipů (A001C001…)" after "Název složky" (UI návrh obr. 1), or nothing.
    public string NameHintSuffix => NameHint is { } hint ? $" — {hint}" : "";

    /// "24 klipů · 1,70 GB · exFAT · čtečka USB 3 · 5 Gb/s".
    public string Facts => owner.SourceFacts(path);

    /// "ALEXA 35 · LogC4" from one clip of the card, or null.
    public string? CameraText => owner.Insight(path)?.Camera?.Text;

    /// "Doplnit" is offered (new clips, the earlier backup reachable).
    public bool CanAppend => AppendText != null;

    /// The disk's connection was looked up (off the UI thread).
    internal void FactsChanged() => Raise(nameof(Facts));

    /// "Zálohováno … — ověřeno" (spec 2.1), or null for a card history does not know.
    public string? HistoryText => owner.HistoryLine(path).Text;
    public HistoryTone HistoryTone => owner.HistoryLine(path).Tone;

    /// A card inspection finished: the history line (and what depends on it) may have changed.
    internal void InsightChanged() => RaiseMany(nameof(HistoryText), nameof(HistoryTone), nameof(CardNamePlaceholder), nameof(NameHint),
        nameof(NameHintSuffix), nameof(AppendText), nameof(AppendNewFiles), nameof(AppendFolderText), nameof(Facts), nameof(CameraText), nameof(CanAppend));

    /// "Doplnit jen nové klipy do dřívější zálohy (12 souborů · 3,2 GB)", or null when there is nothing to append.
    public string? AppendText => owner.AppendOffer(path);

    public bool AppendNewFiles
    {
        get => owner.AppendChosen(path);
        set
        {
            owner.SetAppend(path, value);
            if (value && !IsSelected) IsSelected = true;   // "Doplnit" on the tile also ticks the card
            RaiseMany(nameof(AppendNewFiles), nameof(AppendFolderText));
        }
    }

    /// "Složka: 2026-10-06\A001 na 2 cílech" in place of the name field while appending.
    public string? AppendFolderText => owner.AppendFolder(path);

    /// The date folder or the naming option changed: the suggested name may be another.
    internal void NamingChanged() => RaiseMany(nameof(CardNamePlaceholder), nameof(NameHint), nameof(NameHintSuffix));

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
