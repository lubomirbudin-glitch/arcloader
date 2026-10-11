using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using Offload.Core;
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
        var options = StartupOptions.Parse(e.Args);
        if (options.Demo != null) AppData.UseDemoFolder();   // ruling I3: before anything reads or writes a file (the log too)
        AppData.MigrateFromOffload(AppLog.Write);   // 0.10.0: %APPDATA%\Offload copied to %APPDATA%\ArcLoader once (never with --demo)
        UseEnglish();
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
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            AppLog.Write(ex.ExceptionObject.ToString() ?? "");
            TrayNotifier.Current?.RemoveIcon();   // a crash must not leave a ghost icon
        };
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            AppLog.Write("unobserved task exception: " + ex.Exception);
            ex.SetObserved();
        };
        try
        {
            ThemeWatcher.Start(this);
            var window = new MainWindow(new MainViewModel(options));
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

    /// The app is English only, whatever the PC's language: every element's Language is en-US (bindings, built-in
    /// controls), and the UI thread's culture is en-US with ISO dates (2026-10-10) and 24-hour times — the DatePicker of
    /// a custom date folder takes its format and its month names from it. Background threads keep the PC's culture:
    /// whatever the App and Core format for display already uses InvariantCulture.
    static void UseEnglish()
    {
        var english = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        english.DateTimeFormat.ShortDatePattern = "yyyy-MM-dd";
        english.DateTimeFormat.LongDatePattern = "yyyy-MM-dd";
        english.DateTimeFormat.ShortTimePattern = "HH:mm";
        english.DateTimeFormat.LongTimePattern = "HH:mm:ss";
        CultureInfo.CurrentCulture = english;
        CultureInfo.CurrentUICulture = english;
        CultureInfo.DefaultThreadCurrentUICulture = english;
        // As the default value (not xml:lang on each window): a DatePicker takes CultureInfo.CurrentCulture only while its
        // Language is the default one — an explicit xml:lang="en-US" would bring back the US M/d/yyyy dates. WPF's own
        // default is en-US already; this only makes it independent of that. Never fatal: the texts are English anyway.
        try
        {
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("en-US")));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            AppLog.Write($"language: {e.Message}");
        }
    }

    void Fail(Exception ex)
    {
        try
        {
            MessageBox.Show($"{Product.Name} could not start: {ex.Message}\n\nDetails: {AppLog.Path}", Product.Name,
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
            var text = $"Unexpected application error: {message}\n\nDetails: {AppLog.Path}";
            if (MainWindow is { IsLoaded: true } owner)
                MessageBox.Show(owner, text, Product.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            else
                MessageBox.Show(text, Product.Name, MessageBoxButton.OK, MessageBoxImage.Error);
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
