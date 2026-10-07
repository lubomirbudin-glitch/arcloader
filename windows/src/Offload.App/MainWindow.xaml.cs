using System.Windows;

namespace Offload.App;

public partial class MainWindow : Window
{
    readonly MainViewModel vm;
    readonly VolumeWatcher watcher = new();

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        this.vm = vm;
        DataContext = vm;
        vm.MessageRequested += text => MessageBox.Show(this, text, "Offload");
        Closed += (_, _) => vm.SaveNow();
        Closing += (_, e) => e.Cancel = !vm.TryClose(() => MessageBox.Show(this,
            "Probíhá offload. Opravdu ukončit?\n\nRozpracované soubory se smažou, hotové zůstanou.", "Offload",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);
        // Deferred and once: CloseReady can arrive from a dispatcher callback; Close() inside a Closing event throws.
        bool closed = false;
        Closed += (_, _) => closed = true;
        vm.CloseReady += () => Dispatcher.BeginInvoke(() =>
        {
            if (!closed && IsLoaded) Close();
        });
        vm.AttentionRequested += _ => NativeMethods.Flash(this);
        vm.AskBackedUp = model => BackedUpPromptWindow.Ask(this, model);
        vm.AskYesNo = text => MessageBox.Show(this, text, "Offload", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        vm.AskProjectName = (title, initial, problem) => ProjectNameWindow.Ask(this, title, initial, problem);
        vm.EditLutRulesDialog = (rules, catalog) => LutRulesWindow.Edit(this, rules, catalog);
        // Spec 8a.2: the USB check is on demand only (menu Nástroje, Ctrl+U, the Hardware section) — no start-up question.
        vm.HardwareRequested += hw =>
        {
            if (hardwareWindow is { } open && ReferenceEquals(open.DataContext, hw))
            {
                if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
                open.Activate();
                return;
            }
            hardwareWindow?.Close();
            var w = new HardwareWindow(hw) { Owner = this };
            w.Closed += (_, _) =>
            {
                if (hardwareWindow == w) hardwareWindow = null;
            };
            hardwareWindow = w;
            w.Show();
        };
        // On exit a measurement stops and deletes its temporary file before the process ends (the windows are gone).
        Closed += (_, _) => vm.WaitForHardware();
    }

    HardwareWindow? hardwareWindow;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowSizing.FitToWorkArea(this);
        watcher.Attach(this);
        watcher.Arrived += vm.VolumesArrived;
        watcher.Changed += vm.RefreshVolumes;
        Closed += (_, _) => watcher.Dispose();
    }
}
