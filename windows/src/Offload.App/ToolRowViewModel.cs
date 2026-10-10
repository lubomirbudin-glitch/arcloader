namespace Offload.App;

public sealed class ToolRowViewModel(string name, string purpose, string? path, Action pick, string? downloadUrl = null, string? missing = null)
    : ObservableObject
{
    public string Name => name;
    public string Purpose => purpose;
    /// Found and usable: braw-decode without the Blackmagic RAW library is not (`missing` says what is lacking).
    public bool Found => path != null && missing == null;
    public string PathText => path == null ? "not found" : missing is null ? path : $"{path} — {missing}";
    /// The maker's download page, offered while the tool is not found.
    public string? DownloadUrl => downloadUrl;
    public bool CanDownload => !Found && downloadUrl != null;
    public RelayCommand DownloadCommand { get; } = new(() => { if (downloadUrl != null) Shell.OpenWeb(downloadUrl); });
    public string Glyph => Found ? "\uE930" : "\uE711";
    public RelayCommand PickCommand { get; } = new(pick);
}
