using System.Globalization;
using System.IO;
using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.App;

public sealed partial class MainViewModel
{
    HwTestResult? lastHwTest;
    bool savedMeasured;
    HardwareViewModel? openHardware;
    // The runs form one chain: a run starts only after the previous one ended (one HW test at a time).
    Task hardwareTask = Task.CompletedTask;
    CancellationTokenSource? hardwareCts;

    public RelayCommand OpenHardwareCommand { get; private set; } = null!;

    /// Open (or bring to front) the Hardware window for this view model.
    public event Action<HardwareViewModel>? HardwareRequested;

    void InitHardware()
    {
        lastHwTest = HwTest.Load(HwTest.DefaultPath);
        savedMeasured = lastHwTest?.Measurements.Count > 0;
        OpenHardwareCommand = new RelayCommand(OpenHardware);
    }

    public string HardwareSummary => lastHwTest is { } r
        ? $"Poslední test {r.Time.ToLocalTime().ToString("d. M. yyyy H:mm", CultureInfo.InvariantCulture)} · " +
          Texts.Warnings(r.Advice.Count(a => a.Kind == AdviceKind.Warning))
        : "Zatím neproběhl";

    /// On demand (spec 8a.2: menu Nástroje, Ctrl+U, the Hardware section). An open Hardware window is only
    /// brought to the front — no second run.
    public void OpenHardware()
    {
        if (openHardware is { } open)
        {
            HardwareRequested?.Invoke(open);
            return;
        }
        var hw = new HardwareViewModel(this);
        openHardware = hw;
        HardwareRequested?.Invoke(hw);
        hw.RunQuick();
    }

    /// The Hardware window of `hw` closed: its run is stopped, the next OpenHardware makes a new one.
    internal void HardwareClosed(HardwareViewModel hw)
    {
        hw.Stop();
        if (openHardware == hw) openHardware = null;
    }

    /// A previous run (e.g. of a closed window) is still finishing — a new one waits for it.
    internal bool HardwareFinishing => !hardwareTask.IsCompleted;

    /// Who takes part in the test: the ticked cards and the destinations; with neither, every ready disk by letter
    /// (ByDrive — the drive list itself is read off the UI thread by `HardwareTargets`). No disk is touched here:
    /// destinations come from the already probed rows; one still being probed counts and is checked again later.
    internal (IReadOnlyList<string> Cards, IReadOnlyList<string> Destinations, bool ByDrive) HardwareRoles()
    {
        var cards = volumes.Select(v => v.Path).Concat(manualSources).Where(selected.Contains).ToList();
        var dests = Destinations.Where(d => d.Exists || d.IsChecking).Select(d => d.Root).ToList();
        return cards.Count + dests.Count > 0 ? (cards, dests, false) : ([], [], true);
    }

    /// Off the UI thread: the destinations that exist; with none and no card, every ready disk (ByDrive).
    internal static (IReadOnlyList<string> Destinations, bool ByDrive) HardwareTargets(IReadOnlyList<string> cards,
        IReadOnlyList<string> destinations, bool byDrive)
    {
        var dests = destinations.Where(Directory.Exists).ToList();
        if (!byDrive && cards.Count + dests.Count > 0) return (dests, false);
        var all = DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable && d.IsReady)
            .Select(d => d.RootDirectory.FullName).ToList();
        return (all, true);
    }

    /// Runs `work` off the UI thread after the previous run has ended; exiting the app waits for the chain.
    internal Task<T> RunHardware<T>(Func<T> work, CancellationTokenSource cts)
    {
        var task = hardwareTask.ContinueWith(_ => work(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        hardwareTask = task;
        hardwareCts = cts;
        return task;
    }

    /// On exit (the windows are already closed): stop a measurement and let it delete its temporary file.
    /// Cancelling is checked per 16 MB block, so this is quick; the bound only guards against a hung disk.
    public void WaitForHardware()
    {
        if (hardwareTask.IsCompleted) return;
        try
        {
            hardwareCts?.Cancel();
            if (!hardwareTask.Wait(TimeSpan.FromSeconds(10))) AppLog.Write("hwtest: still running at exit");
        }
        catch (Exception e)
        {
            AppLog.Write($"hwtest at exit: {e.GetBaseException().Message}");
        }
    }

    internal void HardwareTestFinished(HwTestResult result)
    {
        lastHwTest = result;
        Raise(nameof(HardwareSummary));
        // A quick test never replaces a saved measurement on disk (only the summary shows it).
        bool measured = result.Measurements.Count > 0;
        if (!measured && savedMeasured) return;
        savedMeasured = measured;
        Task.Run(() =>
        {
            try
            {
                HwTest.Save(result, HwTest.DefaultPath);
            }
            catch (Exception e)
            {
                AppLog.Write($"hwtest save: {e.Message}");
            }
        });
    }

    internal void SetPortName(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) Settings.PortNames.Remove(id);
        else Settings.PortNames[id] = name.Trim();
        SettingsChanged();
    }
}
