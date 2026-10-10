using System.Windows.Input;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Outputs;

namespace Offload.App;

/// One card in "DNES" and "Historie karet" with its tick for the summary report. One instance per history id, shared by
/// both lists, so a tick shows in both.
public sealed class HistoryRowItem(MainViewModel owner, HistoryRow row) : ObservableObject
{
    HistoryRow row = row;

    public HistoryRow Row
    {
        get => row;
        set
        {
            row = value;
            RaiseMany(nameof(CardName), nameof(Size), nameof(Time), nameof(Status), nameof(Tone), nameof(Project));
        }
    }

    public string Id => row.Id;
    public string CardName => row.CardName;
    public string Size => row.Size;
    public string Time => row.Time;
    public string Status => row.Status;
    public HistoryTone Tone => row.Tone;
    public string? Project => row.Project;

    /// Ticked for the summary report.
    public bool Selected
    {
        get => owner.IsInSummary(Id);
        set => owner.SetInSummary(Id, value);
    }

    internal void SelectedChanged() => Raise(nameof(Selected));
}

/// A day of the history with its rows (HistoryDay with HistoryRowItem).
public sealed record HistoryDayItem(string Title, string Summary, IReadOnlyList<HistoryRowItem> Rows);

/// Task "Souhrnný report z vybraných záloh" (2026-10-09): ticked cards of the history → one summary report (HTML + PDF),
/// made off the UI thread, saved into `<Offload reporty>\souhrn` and, when the user wants, onto a disk they pick; the
/// HTML is opened.
public sealed partial class MainViewModel
{
    readonly HashSet<string> summarySelection = new(StringComparer.Ordinal);
    readonly Dictionary<string, HistoryRowItem> historyRows = new(StringComparer.Ordinal);
    bool makingSummary;

    public RelayCommand SummaryReportCommand { get; private set; } = null!;
    public RelayCommand ClearSummaryCommand { get; private set; } = null!;

    void InitSummaryReport()
    {
        SummaryReportCommand = new RelayCommand(MakeSummaryReport,
            () => !closing && !makingSummary && summarySelection.Count > 0 && Allowed(DemoCommand.SummaryReport));
        ClearSummaryCommand = new RelayCommand(() =>
        {
            var ticked = summarySelection.ToList();
            summarySelection.Clear();
            foreach (var id in ticked)
                if (historyRows.TryGetValue(id, out var item)) item.SelectedChanged();
            SummarySelectionChanged();
        });
    }

    internal bool IsInSummary(string id) => summarySelection.Contains(id);

    internal void SetInSummary(string id, bool on)
    {
        if (!(on ? summarySelection.Add(id) : summarySelection.Remove(id))) return;
        if (historyRows.TryGetValue(id, out var item)) item.SelectedChanged();
        SummarySelectionChanged();
    }

    void SummarySelectionChanged()
    {
        RaiseMany(nameof(HasSummarySelection), nameof(SummaryButtonText));
        CommandManager.InvalidateRequerySuggested();
    }

    public bool HasSummarySelection => summarySelection.Count > 0;

    /// "Souhrnný report (3)…", "Souhrnný report…" with nothing ticked.
    public string SummaryButtonText => summarySelection.Count > 0 ? $"Souhrnný report ({summarySelection.Count})…" : "Souhrnný report…";

    /// The shared row of `row.Id` (made once, then only refreshed).
    HistoryRowItem RowItem(HistoryRow row)
    {
        if (historyRows.TryGetValue(row.Id, out var item))
        {
            if (item.Row != row) item.Row = row;
            return item;
        }
        return historyRows[row.Id] = new HistoryRowItem(this, row);
    }

    HistoryDayItem DayItem(HistoryDay day) => new(day.Title, day.Summary, day.Rows.Select(RowItem).ToList());

    /// After history.json was read again: ticks and rows of entries that are gone are dropped.
    void PruneSummary(IReadOnlyList<HistoryEntry> entries)
    {
        var ids = entries.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var gone in historyRows.Keys.Where(id => !ids.Contains(id)).ToList()) historyRows.Remove(gone);
        if (summarySelection.RemoveWhere(id => !ids.Contains(id)) > 0) SummarySelectionChanged();
    }

    async void MakeSummaryReport()
    {
        if (!Allowed(DemoCommand.SummaryReport)) return;
        var entries = (history ?? []).Where(e => summarySelection.Contains(e.Id)).ToList();
        if (entries.Count == 0)
        {
            ShowMessage(Msg.SummaryNothing);
            return;
        }
        var localRoot = SummaryReport.LocalRoot(Settings.Outputs);
        string? extra = null;
        if (AskYesNo?.Invoke($"Uložit souhrnný report ({entries.Count} {Texts.Plural(entries.Count, "karta", "karty", "karet")}) i na vybraný disk?\n\n"
                + $"Na tomto počítači se uloží vždy: {System.IO.Path.Combine(localRoot, SummaryReport.FolderName)}") == true)
        {
            extra = Dialogs.PickFolder("Vyberte disk nebo složku pro souhrnný report");
            if (extra == null) return;
        }
        makingSummary = true;
        CommandManager.InvalidateRequerySuggested();
        var roots = SummaryReport.LocalRoots(AllSettings);
        var cards = Sources.Select(s => s.Path).ToList();   // never onto a card in a reader
        try
        {
            // Disks may be slow, gone or on the network, the PDF takes seconds: never on the UI thread.
            var saved = await Task.Run(() =>
            {
                var model = SummaryReport.Load(entries.Select(SummaryInput.Of).ToList(), roots);
                return SummaryReport.Save(model, localRoot, extra, m => AppLog.Write($"summary report: {m}"), cards: cards);
            });
            if (closing) return;
            if (saved.ExtraError is string problem) ShowMessage(Msg.SummaryExtraFailed(problem));
            if (Shell.Open(saved.Html) is string error) ShowMessage(error);
        }
        catch (Exception e)
        {
            AppLog.Write($"summary report: {e}");
            if (!closing) ShowMessage(Msg.SummaryFailed(e.Message));
        }
        finally
        {
            makingSummary = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
