using System.Windows;
using Offload.Core.Desktop;

namespace Offload.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // A bug in the UI must never take running offloads down: log it, tell the user, keep going.
        DispatcherUnhandledException += (_, ex) =>
        {
            AppLog.Write(ex.Exception.ToString());
            MessageBox.Show($"Neočekávaná chyba aplikace: {ex.Exception.Message}\n\nPodrobnosti: {AppLog.Path}", "Offload",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => AppLog.Write(ex.ExceptionObject.ToString() ?? "");
        var window = new MainWindow(new MainViewModel(StartupOptions.Parse(e.Args)));
        MainWindow = window;
        window.Show();
    }
}
