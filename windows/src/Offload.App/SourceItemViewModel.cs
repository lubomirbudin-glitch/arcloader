using Offload.Core;

namespace Offload.App;

public enum SourceKind { Card, Drive, Folder }

/// One row in Karty: a volume or a manual folder; the tick and the card name live in MainViewModel.
public sealed class SourceItemViewModel(MainViewModel owner, string path, string title, string detail, SourceKind kind) : ObservableObject
{
    public string Path => path;
    public string Title => title;
    public string Detail => detail;
    public string Glyph => kind switch { SourceKind.Card => "\uE7F1", SourceKind.Drive => "\uEDA2", _ => "\uE8B7" };
    public string CardNamePlaceholder { get; } = SafeDefaultName(path);

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

    public static string SafeDefaultName(string source)
    {
        try
        {
            return Layout.DefaultCardName(source);
        }
        catch (Exception)
        {
            return "";
        }
    }
}
