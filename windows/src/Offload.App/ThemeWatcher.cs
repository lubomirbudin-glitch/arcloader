using System.Security;
using System.Windows;
using Microsoft.Win32;
using Offload.Core.Desktop;

namespace Offload.App;

/// Swaps Themes/Dark.xaml and Themes/Light.xaml (the first merged dictionary of App.xaml) by the Windows setting, at start
/// and whenever the user changes it (WM_SETTINGCHANGE → SystemEvents.UserPreferenceChanged, category General). Reading
/// the setting is a registry read (memory), never a disk.
static class ThemeWatcher
{
    const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    static ResourceDictionary? current;

    public static void Start(Application app)
    {
        var forced = Environment.GetEnvironmentVariable(ThemeChoice.OverrideVariable);
        if (ThemeChoice.IsOverride(forced))
        {
            // Screenshots: the whole app (also the Fluent controls) in the theme asked for.
            var theme = ThemeChoice.Resolve(forced, null);
            app.ThemeMode = theme == AppTheme.Dark ? ThemeMode.Dark : ThemeMode.Light;   // WPF0001 is off in the csproj
            Apply(app, theme);
            return;
        }
        Apply(app, ThemeChoice.Resolve(null, ReadSetting()));
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General) app.Dispatcher.BeginInvoke(() => Apply(app, ThemeChoice.Resolve(null, ReadSetting())));
        };
    }

    static object? ReadSetting()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme");
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            AppLog.Write($"theme: {e.Message}");
            return null;
        }
    }

    static void Apply(Application app, AppTheme theme)
    {
        var source = new Uri($"pack://application:,,,/Themes/{theme}.xaml", UriKind.Absolute);
        if (current?.Source == source) return;
        var next = new ResourceDictionary { Source = source };
        var merged = app.Resources.MergedDictionaries;
        if (current != null) merged.Remove(current);
        merged.Insert(0, next);
        current = next;
    }
}
