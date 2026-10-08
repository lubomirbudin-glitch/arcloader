namespace Offload.Core.Desktop;

public enum AppTheme { Dark, Light }

/// Dark or light (UI návrh, rozhodnutí 2026-10-07): the Windows setting "Režim aplikací" decides; the environment variable
/// OFFLOAD_THEME (dark / light) wins — for screenshots and checks only, off by default.
public static class ThemeChoice
{
    public const string OverrideVariable = "OFFLOAD_THEME";

    /// `appsUseLightTheme`: HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme — 0 = dark,
    /// anything else or missing = light (the Windows default).
    public static AppTheme Resolve(string? overrideValue, object? appsUseLightTheme) => overrideValue?.Trim().ToLowerInvariant() switch
    {
        "dark" => AppTheme.Dark,
        "light" => AppTheme.Light,
        _ => appsUseLightTheme is int i && i == 0 ? AppTheme.Dark : AppTheme.Light,
    };

    /// True when OFFLOAD_THEME names a theme (then the whole app, also the Fluent controls, uses it).
    public static bool IsOverride(string? overrideValue) => overrideValue?.Trim().ToLowerInvariant() is "dark" or "light";
}
