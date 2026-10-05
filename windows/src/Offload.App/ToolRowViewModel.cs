namespace Offload.App;

public sealed class ToolRowViewModel(string name, string purpose, string? path, Action pick) : ObservableObject
{
    public string Name => name;
    public string Purpose => purpose;
    public bool Found => path != null;
    public string PathText => path ?? "nenalezeno";
    public string Glyph => Found ? "\uE930" : "\uE711";
    public RelayCommand PickCommand { get; } = new(pick);
}
