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
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        watcher.Attach(this);
        watcher.Changed += vm.RefreshVolumes;
        Closed += (_, _) => watcher.Dispose();
    }
}
