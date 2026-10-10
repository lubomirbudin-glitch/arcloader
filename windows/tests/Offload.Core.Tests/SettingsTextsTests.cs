using Offload.Core.Desktop;
using Offload.Core.Notify;

namespace Offload.Core.Tests;

/// The settings page (UI návrh obr. 4): the LUT table, the bitrate hint, the burn-in frame, the notification summary.
public class SettingsTextsTests
{
    static readonly LutCatalog Cat = LutCatalog.Load(LutCatalog.DefaultFolder);

    [Fact]
    public void LutRows_MakerAndCurve_ChosenMarked()
    {
        var p = new ProxySettings { LutMode = LutMode.Builtin, LutBuiltin = "arri-logc4-awg4" };
        var rows = Cat.Luts.Select(l => SettingsTexts.Row(l, p)).ToDictionary(r => r.Id);

        Assert.Equal(("ARRI · LogC4", "LogC4 / AWG4 → Rec.709", "in use"),
            (rows["arri-logc4-awg4"].Title, rows["arri-logc4-awg4"].Detail, rows["arri-logc4-awg4"].Marker));
        Assert.Equal(("Sony · S-Log3", "S-Log3 / S-Gamut3.Cine → Rec.709", (string?)null),
            (rows["sony-slog3-sgamut3cine"].Title, rows["sony-slog3-sgamut3cine"].Detail, rows["sony-slog3-sgamut3cine"].Marker));
        Assert.Equal("Blackmagic · Film Gen 5", rows["bmd-film-gen5"].Title);
        Assert.Equal("Film Gen 5 → Rec.709", rows["bmd-film-gen5"].Detail);
        Assert.Equal("Canon · C-Log3", rows["canon-clog3-cinemagamut"].Title);
        Assert.All(Cat.Luts.Select(l => SettingsTexts.Row(l, new ProxySettings())), r => Assert.Null(r.Marker));   // automatic: none marked
    }

    [Fact]
    public void Bitrate_Frame_Notify()
    {
        Assert.Equal("≈ 75 MB / minute", SettingsTexts.BitrateHint(10));
        Assert.Equal("≈ 15 MB / minute", SettingsTexts.BitrateHint(2));

        Assert.Null(SettingsTexts.Frame(new ProxySettings()));
        Assert.Equal("full frame", SettingsTexts.FrameText(new ProxySettings()));
        var crop = new ProxySettings { FrameAspect = 2.39, FrameScale = 0.9 };
        var (x, y, w, h) = SettingsTexts.Frame(crop)!.Value;
        Assert.Equal(0.05, x, 2);
        Assert.Equal(0.9, w, 2);
        Assert.Equal(1 - 2 * y, h, 2);
        Assert.Equal("2.39 · 90 %", SettingsTexts.FrameText(crop));

        Assert.Equal("Windows: on · phone (ntfy): off", SettingsTexts.NotifySummary(new NotifySettings()));
        Assert.Equal("Windows: off · phone (ntfy): film-x",
            SettingsTexts.NotifySummary(new NotifySettings { Windows = false, Ntfy = new NtfySettings { Enabled = true, Topic = "film-x" } }));
    }
}
