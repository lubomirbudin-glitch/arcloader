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
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        watcher.Attach(this);
        watcher.Changed += vm.RefreshVolumes;
        Closed += (_, _) => watcher.Dispose();
    }
}
