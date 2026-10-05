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
        vm.HardwareRequested += hw =>
        {
            hardwareWindow?.Close();
            var w = new HardwareWindow(hw) { Owner = this };
            w.Closed += (_, _) =>
            {
                if (hardwareWindow == w) hardwareWindow = null;
            };
            hardwareWindow = w;
            w.Show();
        };
        // On exit a measurement finishes the disk it is on, so its temporary file is deleted (the windows are gone).
        Closed += (_, _) => vm.WaitForHardware();
        // Spec 8a.2: asked at every start — once the window is drawn, so the question sits on top of it.
        ContentRendered += (_, _) =>
        {
            if (vm.Options.NoHwPrompt || promptShown) return;
            promptShown = true;
            var answer = MessageBox.Show(this,
                "Prověřit USB porty?\n\nZjistím, kde a jak rychle jsou připojené karty a disky, a poradím, kam co zapojit. Nic se nezapisuje.",
                "Offload", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes && !closed) vm.OpenHardware();
        };
    }

    HardwareWindow? hardwareWindow;
    bool promptShown;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        watcher.Attach(this);
        watcher.Changed += vm.RefreshVolumes;
        Closed += (_, _) => watcher.Dispose();
    }
}
