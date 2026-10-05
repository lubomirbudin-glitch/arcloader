using System.Windows;

namespace Offload.App;

public partial class MainWindow : Window
{
    readonly MainViewModel vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        this.vm = vm;
        DataContext = vm;
        vm.MessageRequested += text => MessageBox.Show(this, text, "Offload");
        Closed += (_, _) => vm.SaveNow();
    }
}
