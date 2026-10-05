using System.Windows;

namespace Offload.App;

public partial class HardwareWindow : Window
{
    public HardwareWindow(HardwareViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.Confirm = text => MessageBox.Show(this, text, "Offload", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        Closed += (_, _) => vm.WindowClosed();   // a running measurement stops at once and deletes its temporary file
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowSizing.FitToWorkArea(this);
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
