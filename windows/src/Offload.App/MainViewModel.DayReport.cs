using System.Windows.Input;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Outputs;

namespace Offload.App;

/// Nástroje → Denní report… (spec 3.2): a day folder on a destination (or a card folder in it) → the day report is made
/// again off the UI thread and its HTML opened.
public sealed partial class MainViewModel
{
    bool makingDayReport;

    public RelayCommand DayReportCommand { get; private set; } = null!;

    void InitDayReport() => DayReportCommand = new RelayCommand(PickAndMakeDayReport, () => !closing && !makingDayReport);

    async void PickAndMakeDayReport()
    {
        if (Dialogs.PickFolder("Vyberte složku dne na cíli") is not string folder) return;
        makingDayReport = true;
        var outputs = SettingsStore.Clone(Settings.Outputs);
        try
        {
            // The folder may be on a slow or network disk, the PDF takes seconds, another writer may hold the day:
            // never on the UI thread.
            var html = await Task.Run(() => DayReportService.RegenerateDay(DayReportService.DayFolderOf(folder), outputs, m => AppLog.Write($"day report: {m}")));
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
