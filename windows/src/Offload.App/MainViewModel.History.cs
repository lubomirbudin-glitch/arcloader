using Offload.Core.Desktop;
using Offload.Core.History;

namespace Offload.App;

/// Spec 2.1: under every card the line "Zálohováno …" from history.json. history.json is read off the UI thread at start
/// and after every finished copy; each source is looked at (scan + fingerprint + match) by its own Task.
public sealed partial class MainViewModel
{
    readonly CardInspections inspections = new();
    HistoryStore historyStore = null!;
    // Null until loaded: the rows say "Zjišťuji…" meanwhile.
    IReadOnlyList<HistoryEntry>? history;
    bool historyLoading, historyReload;

    void InitHistory()
    {
        historyStore = HistoryStore.Default(m => AppLog.Write(m));
        ReloadHistory();
    }

    /// Reads history.json off the UI thread, then looks at every card again; a request during a read runs once more.
    void ReloadHistory()
    {
        if (historyLoading)
        {
            historyReload = true;
            return;
        }
        historyLoading = true;
        var store = historyStore;
        Task.Run(() => store.Load()).ContinueWith(t =>
        {
            historyLoading = false;
            if (closing) return;
            if (t.IsCompletedSuccessfully) history = t.Result;
            else
            {
                AppLog.Write($"history load: {t.Exception?.GetBaseException().Message}");
                history ??= [];
            }
            InspectAll();
            if (historyReload)
            {
                historyReload = false;
                ReloadHistory();
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    void InspectAll()
    {
        foreach (var s in Sources) Inspect(s.Path);
    }

    /// Only the sources without a result for the card in them now (new cards, swapped cards).
    void InspectMissing()
    {
        foreach (var s in Sources)
            if (inspections.Result(s.Path, IdentityOf(s.Path)) == null && !inspections.IsRunning(s.Path)) Inspect(s.Path);
    }

    void Inspect(string path)
    {
        if (history is not { } entries || closing) return;   // InspectAll runs once history is loaded
        if (inspections.Start(path, IdentityOf(path)) is not CancellationToken ct) return;
        var store = historyStore;
        Task.Run(() => CardInspector.Inspect(path, entries, store, ct), ct).ContinueWith(t =>
        {
            if (t.IsFaulted) AppLog.Write($"card inspect {path}: {t.Exception?.GetBaseException().Message}");
            bool listed = Sources.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
            bool again = inspections.Finished(path, t.IsCompletedSuccessfully ? t.Result : null, IdentityOf(path), listed);
            if (closing) return;
            RowFor(path)?.InsightChanged();
            SetupChanged?.Invoke();
            if (again) Inspect(path);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// The card in a scanned volume (`CardIdentity` from the scan); null for a manual folder.
    string? IdentityOf(string path) =>
        volumes.FirstOrDefault(v => string.Equals(v.Path, path, StringComparison.OrdinalIgnoreCase))?.Identity;

    SourceItemViewModel? RowFor(string path) =>
        Sources.FirstOrDefault(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));

    /// The last look at the card that is at `path` now, or null.
    internal CardInsight? Insight(string path) => inspections.Result(path, IdentityOf(path));

    internal (string? Text, HistoryTone Tone) HistoryLine(string path) =>
        CardInspections.RowText(history != null, inspections.IsRunning(path), Insight(path));
}
