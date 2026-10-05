namespace Offload.Core.Desktop;

public sealed record WindowPlacement(bool Maximize, double Width, double Height, double Left, double Top);

/// A window never bigger than the work area (a 1366×768 laptop has ~728 px): the size is clamped (never below the
/// minimum — if even that does not fit, the window starts maximized) and it is centred on the work area or on
/// `center` (the owner window), kept inside the work area. All values in WPF device-independent units.
public static class WindowFit
{
    /// A monitor's work area (device pixels, as GetMonitorInfo gives it) in WPF units: `toDipX/Y` are the window's
    /// TransformFromDevice factors (1 / DPI scale).
    public static (double Left, double Top, double Width, double Height) FromDevice(int left, int top, int right, int bottom, double toDipX, double toDipY) =>
        (left * toDipX, top * toDipY, (right - left) * toDipX, (bottom - top) * toDipY);

    public static WindowPlacement Fit(double areaLeft, double areaTop, double areaWidth, double areaHeight,
        double width, double height, double minWidth, double minHeight, double? centerX = null, double? centerY = null)
    {
        if (minWidth > areaWidth || minHeight > areaHeight)
            return new WindowPlacement(true, width, height, areaLeft, areaTop);
        double w = Math.Min(width, areaWidth), h = Math.Min(height, areaHeight);
        double left = (centerX ?? areaLeft + areaWidth / 2) - w / 2;
        double top = (centerY ?? areaTop + areaHeight / 2) - h / 2;
        left = Math.Clamp(left, areaLeft, areaLeft + areaWidth - w);
        top = Math.Clamp(top, areaTop, areaTop + areaHeight - h);
        return new WindowPlacement(false, w, h, left, top);
    }
}
