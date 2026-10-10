using System.Windows;
using Offload.Core;

namespace Offload.App;

/// Tools → Updates… and the "New version" link (spec 4.4): the manifest source, "Check now" (off the UI
/// thread), the new version's notes and "Open".
public partial class UpdateWindow : Window
{
    readonly MainViewModel vm;

    UpdateWindow(MainViewModel vm)
    {
        InitializeComponent();
        this.vm = vm;
        DataContext = vm;
        Current.Text = $"This version: {OffloadVersion.String}";
    }

    public static void Show(Window owner, MainViewModel vm) => new UpdateWindow(vm) { Owner = owner }.ShowDialog();

    async void Check_Click(object sender, RoutedEventArgs e)
    {
        Manifest.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();   // the address as typed
        CheckButton.IsEnabled = false;
        Result.Text = "Checking…";
        try
        {
            Result.Text = await vm.CheckUpdatesAsync(force: true) ?? "";
        }
        finally
        {
            CheckButton.IsEnabled = true;
        }
    }

    void Open_Click(object sender, RoutedEventArgs e) => vm.OpenNewVersion();
}
