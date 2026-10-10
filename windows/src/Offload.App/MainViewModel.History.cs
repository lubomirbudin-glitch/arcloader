using Offload.Core;
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
        if (IsDemo) return;   // the scenario's history
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
            if (t.IsCompletedSuccessfully)
            {
                history = t.Result;
                PruneSummary(t.Result);
            }
            else
            {
                AppLog.Write($"history load: {t.Exception?.GetBaseException().Message}");
                history ??= [];
            }
            RaiseMany(nameof(Today), nameof(HistoryDays));   // the right column's DNES and the history tab
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
        if (IsDemo || history is not { } entries || closing) return;   // InspectAll runs once history is loaded
        if (inspections.Start(path, IdentityOf(path)) is not CancellationToken ct) return;
        var store = historyStore;
        Task.Run(() => CardInspector.Inspect(path, entries, store, ct), ct).ContinueWith(t =>
        {
            if (t.IsFaulted) AppLog.Write($"card inspect {path}: {t.Exception?.GetBaseException().Message}");
            bool listed = Sources.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
            bool again = inspections.Finished(path, t.IsCompletedSuccessfully ? t.Result : null, IdentityOf(path), listed);
            if (closing) return;
            PruneAppend();
            RowFor(path)?.InsightChanged();
            SetupChanged?.Invoke();
            if (again) Inspect(path);
            else if (t.IsCompletedSuccessfully && t.Result is { Error: null } looked) ProbeCamera(path, looked);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// Minor M11: once the look is published, one clip names the camera (UI návrh 6) — off the UI thread, reading only,
    /// bounded to 5 s (CameraHints.ProbeTimeout for ffprobe, the same as a cancel before it starts); the label is kept
    /// only while that look is still the card's (CardInspections.SetCamera).
    void ProbeCamera(string path, CardInsight looked)
    {
        var tools = resolvedTools;
        var limit = new CancellationTokenSource(CameraHints.ProbeTimeout);
        Task.Run(() => CameraHints.Probe(looked, tools, LutCatalog.Default, limit.Token), limit.Token).ContinueWith(t =>
        {
            limit.Dispose();
            if (t.IsFaulted) AppLog.Write($"camera probe {path}: {t.Exception?.GetBaseException().Message}");
            if (closing || !t.IsCompletedSuccessfully || !inspections.SetCamera(path, looked, t.Result)) return;
            RowFor(path)?.InsightChanged();
            SetupChanged?.Invoke();   // step 3 names the LUT's curve from the camera
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// The card in a scanned volume (`CardIdentity` from the scan); null for a manual folder.
    string? IdentityOf(string path) =>
        volumes.FirstOrDefault(v => string.Equals(v.Path, path, StringComparison.OrdinalIgnoreCase))?.Identity;

    SourceItemViewModel? RowFor(string path) =>
        Sources.FirstOrDefault(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));

    string queueTab = "queue";

    /// "queue" (Fronta) or "history" (Historie karet).
    public string QueueTab
    {
        get => queueTab;
        set => Set(ref queueTab, value ?? "queue");
    }

    /// DNES under the queue (UI návrh 7): the cards finished today, from history.json as loaded; null hides it.
    public HistoryDayItem? Today => history is { } h && HistoryView.Today(h, DateTime.Now) is { } day ? DayItem(day) : null;

    /// The history tab: every day with a card, newest first; a card can be ticked for the summary report.
    public IReadOnlyList<HistoryDayItem> HistoryDays => history is { } h ? HistoryView.Days(h, DateTime.Now).Select(DayItem).ToList() : [];

    /// The last look at the card that is at `path` now, or null.
    internal CardInsight? Insight(string path) => inspections.Result(path, IdentityOf(path));

    internal (string? Text, HistoryTone Tone) HistoryLine(string path) =>
        CardInspections.RowText(history != null, inspections.IsRunning(path), Insight(path));
}
