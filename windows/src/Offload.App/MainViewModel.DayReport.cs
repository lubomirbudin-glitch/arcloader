using System.Windows.Input;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Outputs;

namespace Offload.App;

/// Tools → Day report… (spec 3.2): a day folder on a destination (or a card folder in it) → the day report is made
/// again off the UI thread and its HTML opened.
public sealed partial class MainViewModel
{
    bool makingDayReport;

    public RelayCommand DayReportCommand { get; private set; } = null!;

    void InitDayReport() => DayReportCommand = new RelayCommand(PickAndMakeDayReport, () => !closing && !makingDayReport && Allowed(DemoCommand.DayReport));

    async void PickAndMakeDayReport()
    {
        if (!Allowed(DemoCommand.DayReport)) return;
        if (Dialogs.PickFolder("Choose a day folder on a destination") is not string folder) return;
        makingDayReport = true;
        var all = SettingsStore.Clone(AllSettings);   // a snapshot for the background thread
        try
        {
            // The folder may be on a slow or network disk, the PDF takes seconds, another writer may hold the day:
            // never on the UI thread.
            var html = await Task.Run(() =>
            {
                var day = DayReportService.DayFolderOf(folder);
                var outputs = DayReportService.OutputsFor(day, all);
                var made = DayReportService.RegenerateDay(day, outputs, m => AppLog.Write($"day report: {m}"));
                // Task 2026-10-09: the day report on this computer too (a failure is only logged — the report itself is made).
                if (LocalReports.RootFor(outputs) is string local)
                {
                    try { LocalReports.MirrorDay([day], local); }
                    catch (Exception e) { AppLog.Write($"day report local copy: {e.Message}"); }
                }
                return made;
            });
            if (closing) return;
            if (Shell.Open(html) is string error) ShowMessage(error);
        }
        catch (Exception e)
        {
            AppLog.Write($"day report {folder}: {e}");
            if (!closing) ShowMessage(Msg.DayReportFailed(e.Message));
        }
        finally
        {
            makingDayReport = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
