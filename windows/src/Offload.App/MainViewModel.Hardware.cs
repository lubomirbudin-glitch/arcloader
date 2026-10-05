using System.Globalization;
using System.IO;
using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.App;

public sealed partial class MainViewModel
{
    HwTestResult? lastHwTest;
    Task? hardwareTask;
    CancellationTokenSource? hardwareCts;

    public RelayCommand OpenHardwareCommand { get; private set; } = null!;

    /// Open (or bring to front) the Hardware window for this view model.
    public event Action<HardwareViewModel>? HardwareRequested;

    void InitHardware()
    {
        lastHwTest = HwTest.Load(HwTest.DefaultPath);
        OpenHardwareCommand = new RelayCommand(OpenHardware);
    }

    public string HardwareSummary => lastHwTest is { } r
        ? $"Poslední test {r.Time.ToLocalTime().ToString("d. M. yyyy H:mm", CultureInfo.InvariantCulture)} · " +
          Texts.Warnings(r.Advice.Count(a => a.Kind == AdviceKind.Warning))
        : "Zatím neproběhl";

    public void OpenHardware()
    {
        var hw = new HardwareViewModel(this);
        HardwareRequested?.Invoke(hw);
        hw.RunQuick();
    }

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

    /// A test is running (quick or measuring): remembered so that exiting the app lets a measurement finish the
    /// disk it is on — its 1 GB temporary file is deleted only when that disk is done.
    internal void HardwareStarted(Task task, CancellationTokenSource cts)
    {
        hardwareTask = task;
        hardwareCts = cts;
    }

    /// On exit (the windows are already closed): stop a measurement and wait for the current disk's temporary file
    /// to be deleted — bounded, so a hung disk never keeps the process alive for ever.
    public void WaitForHardware()
    {
        if (hardwareTask is not { IsCompleted: false } task) return;
        try
        {
            hardwareCts?.Cancel();
            if (!task.Wait(TimeSpan.FromMinutes(3))) AppLog.Write("hwtest: still running at exit");
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
