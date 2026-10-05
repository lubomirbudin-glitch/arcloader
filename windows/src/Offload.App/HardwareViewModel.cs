using System.Collections.ObjectModel;
using System.Globalization;
using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.App;

public sealed record HwDriveRow(string Role, string Root, string Description);
public sealed record HwAdviceRow(string Glyph, string Kind, string Text);
public sealed record HwMeasureRow(string Role, string Root, string Read, string Write);

public sealed class HwPortRow(HardwareViewModel owner, UsbConnector connector, string kind, string device, string drives, string name) : ObservableObject
{
    public string Label => PortLabels.WithKind(connector);
    public string Kind => kind;
    public string Device => device;
    public string Drives => drives;
    string customName = name;
    public string CustomName
    {
        get => customName;
        set
        {
            value ??= "";
            if (value == customName) return;
            customName = value;
            owner.Rename(connector, value);
            Raise();
        }
    }
}

public sealed record HwPortGroup(string Controller, IReadOnlyList<HwPortRow> Ports);

/// The Hardware window (spec 8a.2): where the cards and disks are connected, advice, optional measurement,
/// the USB ports with user names. Tests run off the UI thread; nothing is ever written to a card.
public sealed class HardwareViewModel : ObservableObject
{
    readonly MainViewModel owner;
    CancellationTokenSource? cts;
    HwTestResult? result;
    bool busy, showInternal, byDrive, stopping;
    string status = "";

    public HardwareViewModel(MainViewModel owner)
    {
        this.owner = owner;
        MeasureCommand = new RelayCommand(Measure, () => !Busy && !byDrive && result != null);
        StopCommand = new RelayCommand(Stop, () => Busy && !stopping);
        RerunCommand = new RelayCommand(RunQuick, () => !Busy);
    }

    public ObservableCollection<HwDriveRow> Drives { get; } = [];
    public ObservableCollection<HwAdviceRow> Advice { get; } = [];
    public ObservableCollection<HwMeasureRow> Measurements { get; } = [];
    public ObservableCollection<HwPortGroup> PortGroups { get; } = [];
    public RelayCommand MeasureCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand RerunCommand { get; }

    /// Asks before measuring (the window shows a Yes/No message box owned by itself). Unset = do not measure.
    public Func<string, bool>? Confirm { get; set; }

    public bool Busy { get => busy; private set => Set(ref busy, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public bool HasMeasurements => Measurements.Count > 0;
    public string MeasureHint => byDrive ? "Pro měření vyberte kartu nebo přidejte cíl." : "";

    public bool ShowInternal
    {
        get => showInternal;
        set { if (Set(ref showInternal, value)) BuildPorts(); }
    }

    public void RunQuick() => Run(measure: false);

    /// The window closed: the run stops and the next "Prověřit USB porty…" opens a new window.
    public void WindowClosed() => owner.HardwareClosed(this);

    /// Zastavit, or the window closed: the run stops at once (a measurement deletes its temporary file);
    /// "Zastavuji…" until the task has really ended.
    public void Stop()
    {
        if (!Busy || stopping || cts is not { } c) return;
        stopping = true;
        c.Cancel();
        Status = "Zastavuji…";
    }

    void Measure()
    {
        var (_, dests, _) = owner.HardwareRoles();
        var list = dests.Count > 0 ? string.Join("\n", dests.Select(d => "   " + d)) : "   (žádné)";
        var text = "Změřit rychlost?\n\nZ karet se jen čte (až 1 GB z každé).\n" +
                   $"Na cílové disky se zapíše a přečte dočasný soubor 1 GB a hned se smaže:\n{list}\n\n" +
                   "Podle rychlosti disků to může trvat několik minut.";
        if (Confirm?.Invoke(text) == true) Run(measure: true);
    }

    void Run(bool measure)
    {
        if (Busy) return;
        var (cards, dests, all) = owner.HardwareRoles();
        var names = new Dictionary<string, string>(owner.Settings.PortNames, StringComparer.OrdinalIgnoreCase);
        cts = new CancellationTokenSource();
        var token = cts.Token;
        Busy = true;
        var running = measure ? "Měřím rychlost… (může trvat několik minut)" : "Zjišťuji připojení…";
        bool waits = owner.HardwareFinishing;
        Status = waits ? "Čekám, až doběhne předchozí test…" : running;
        var task = owner.RunHardware(() =>
        {
            if (waits) Dispatch(() => { if (!stopping) Status = running; });   // the previous run has ended
            var (targets, byDriveNow) = MainViewModel.HardwareTargets(cards, dests, all);
            // Never measure by drive letter: every disk would get a 1 GB file (the Measure button is off then anyway).
            var r = HwTest.Run(cards, targets, measure && !byDriveNow, portNames: names, rolesByDrive: byDriveNow, cancel: token);
            return (Result: r, ByDrive: byDriveNow);
        }, cts);
        task.ContinueWith(t =>
        {
            try
            {
                Busy = false;
                stopping = false;
                if (t.IsCompletedSuccessfully)
                {
                    result = t.Result.Result;
                    byDrive = t.Result.ByDrive;
                    Status = token.IsCancellationRequested && measure ? "Měření přerušeno — zobrazeno, co se stihlo změřit." : "";
                    Rebuild();
                    if (!token.IsCancellationRequested) owner.HardwareTestFinished(result);
                }
                else
                {
                    Status = "Test se nepodařil: " + t.Exception?.GetBaseException().Message;
                    AppLog.Write($"hwtest: {t.Exception?.GetBaseException()}");
                }
            }
            catch (Exception e)
            {
                Status = "Test se nepodařil: " + e.Message;
                AppLog.Write($"hwtest show: {e}");
            }
            RaiseMany(nameof(MeasureHint), nameof(HasMeasurements));
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    void Rebuild()
    {
        if (result is not { } r) return;
        Measurements.Clear();
        foreach (var m in r.Measurements) Measurements.Add(new HwMeasureRow(m.Role, m.Root, Mb(m.ReadMBps), Mb(m.WriteMBps)));
        RebuildNamed();
        BuildPorts();
    }

    /// What shows port names: the drive lines and the advice.
    void RebuildNamed()
    {
        if (result is not { } r) return;
        var names = owner.Settings.PortNames;
        Drives.Clear();
        foreach (var d in r.Drives) Drives.Add(new HwDriveRow(d.Role, d.Link.Root, d.Link.Describe(r.Connectors, names)));
        Advice.Clear();
        foreach (var a in HwTest.Advise(r.Drives, r.Connectors, r.Measurements, names))
            Advice.Add(a.Kind switch
            {
                AdviceKind.Warning => new HwAdviceRow("\uE7BA", "Warning", a.Text),
                AdviceKind.Info => new HwAdviceRow("\uE946", "Info", a.Text),
                _ => new HwAdviceRow("\uE82F", "Tip", a.Text),
            });
    }

    void BuildPorts()
    {
        PortGroups.Clear();
        if (result is not { } r) return;
        var names = owner.Settings.PortNames;
        foreach (var g in r.Connectors.Where(c => c.UserVisible || ShowInternal).GroupBy(c => c.ControllerId, StringComparer.OrdinalIgnoreCase))
        {
            var rows = g.Select(c =>
            {
                var device = c.Device is { } dev ? $"{dev.Name} — {UsbSpeeds.Label(dev.Link)}" : "volný";
                var drives = string.Join(", ", r.Drives.Where(d => d.Link.Usb is { } u && c.Has(u.ConnectorId))
                    .Select(d => d.Role == d.Link.Root.TrimEnd('\\') ? d.Role : $"{d.Link.Root.TrimEnd('\\')} ({d.Role})"));
                return new HwPortRow(this, c, c.Kind, device, drives, UserName(c, names));
            }).ToList();
            PortGroups.Add(new HwPortGroup(PortLabels.ShortController(g.First().ControllerName), rows));
        }
    }

    /// The user's name of a socket as `PortLabels.Name` finds it (its id, then the other half of a USB 3 hub).
    static string UserName(UsbConnector c, IReadOnlyDictionary<string, string> names) =>
        new[] { c.Id }.Concat(c.AltIds).Select(id => names.GetValueOrDefault(id)).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "";

    internal void Rename(UsbConnector connector, string name)
    {
        owner.SetPortName(connector.Id, name);
        foreach (var alt in connector.AltIds) owner.SetPortName(alt, "");   // one name per socket, under its id
        RebuildNamed();
    }

    static void Dispatch(Action action) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(action);

    static string Mb(double? v) => v is double x ? x.ToString("0", CultureInfo.InvariantCulture) + " MB/s" : "—";
}
