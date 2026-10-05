using System.Windows;

namespace Offload.App;

public partial class HardwareWindow : Window
{
    public HardwareWindow(HardwareViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.Confirm = text => MessageBox.Show(this, text, "Offload", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        Closing += (_, _) => vm.Stop();   // a running measurement stops after the current disk
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
