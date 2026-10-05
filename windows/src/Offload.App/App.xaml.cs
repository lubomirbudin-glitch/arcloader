using System.Windows;
using Offload.Core.Desktop;

namespace Offload.App;

public partial class App : Application
{
    static bool showing;
    static string? lastMessage;
    static DateTime lastShown = DateTime.MinValue;
    bool started;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // A bug in the UI must never take running offloads down: log it, tell the user, keep going.
        DispatcherUnhandledException += (_, ex) =>
        {
            AppLog.Write(ex.Exception.ToString());
            ex.Handled = true;
            if (!started)
            {
                // Failure before the window was shown: nothing to keep alive.
                Fail(ex.Exception);
                return;
            }
            ShowError(ex.Exception.Message);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => AppLog.Write(ex.ExceptionObject.ToString() ?? "");
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            AppLog.Write("unobserved task exception: " + ex.Exception);
            ex.SetObserved();
        };
        try
        {
            var window = new MainWindow(new MainViewModel(StartupOptions.Parse(e.Args)));
            MainWindow = window;
            window.Show();
            started = true;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex.ToString());
            Fail(ex);
        }
    }

    void Fail(Exception ex)
    {
        try
        {
            MessageBox.Show($"Offload se nepodařilo spustit: {ex.Message}\n\nPodrobnosti: {AppLog.Path}", "Offload",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception boxEx)
        {
            AppLog.Write("message box: " + boxEx.Message);
        }
        Shutdown(1);
    }

    void ShowError(string message)
    {
        // One dialog at a time, and the same message at most once per 10 s; the rest is only logged.
        if (showing || (message == lastMessage && DateTime.UtcNow - lastShown < TimeSpan.FromSeconds(10))) return;
        showing = true;
        lastMessage = message;
        lastShown = DateTime.UtcNow;
        try
        {
            var text = $"Neočekávaná chyba aplikace: {message}\n\nPodrobnosti: {AppLog.Path}";
            if (MainWindow is { IsLoaded: true } owner)
                MessageBox.Show(owner, text, "Offload", MessageBoxButton.OK, MessageBoxImage.Error);
            else
                MessageBox.Show(text, "Offload", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception boxEx)
        {
            AppLog.Write("message box: " + boxEx.Message);
        }
        finally
        {
            showing = false;
            lastShown = DateTime.UtcNow;
        }
    }
}
