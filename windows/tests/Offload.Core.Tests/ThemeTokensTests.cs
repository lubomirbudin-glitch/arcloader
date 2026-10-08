using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using Offload.Core.Desktop;

namespace Offload.Core.Tests;

/// The theme of the new look (UI návrh, „Design tokeny“; rozhodnutí: tmavé i světlé podle Windows): both token sets have
/// the same keys, the dark one is the design's, every text colour is readable on its backgrounds, and every theme
/// dictionary of Offload.App loads (XamlReader — the GUI itself is never started).
public class ThemeTokensTests
{
    static string Themes
    {
        get
        {
            var d = AppContext.BaseDirectory;
            while (d != null && !File.Exists(Path.Combine(d, "Directory.Build.props"))) d = Path.GetDirectoryName(d.TrimEnd('\\'));
            return Path.Combine(d ?? throw new InvalidOperationException("Directory.Build.props not found"), "src", "Offload.App", "Themes");
        }
    }

    static Dictionary<string, string> Tokens(string file)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(Path.Combine(Themes, file)).Root!.Elements()
            .ToDictionary(e => (string)e.Attribute(x + "Key")!, e => (string)e.Attribute("Color")!);
    }

    static double Luminance(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 8) h = h[2..];   // alpha is not part of these checks
        double C(int i)
        {
            double c = int.Parse(h.Substring(i, 2), NumberStyles.HexNumber) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * C(0) + 0.7152 * C(2) + 0.0722 * C(4);
    }

    static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Fact]
    public void DarkAndLight_SameKeys_ValidColours()
    {
        var dark = Tokens("Dark.xaml");
        var light = Tokens("Light.xaml");
        Assert.Equal(dark.Keys.Order(), light.Keys.Order());
        Assert.All(dark.Values.Concat(light.Values), v => Assert.Matches("^#([0-9A-F]{6}|[0-9A-F]{8})$", v));
    }

    [Theory]
    [InlineData("Bg", "#0E1012")]
    [InlineData("BgHeader", "#121518")]
    [InlineData("Panel", "#14171A")]
    [InlineData("Raised", "#1B1F23")]
    [InlineData("RaisedAlt", "#171A1E")]
    [InlineData("Input", "#0F1215")]
    [InlineData("Line", "#262B31")]
    [InlineData("LineSoft", "#22272C")]
    [InlineData("Track", "#2A3038")]
    [InlineData("Text", "#ECEFF2")]
    [InlineData("Text2", "#A7AFB8")]
    [InlineData("Text3", "#808994")]
    [InlineData("Accent", "#2366D1")]
    [InlineData("AccentText", "#7DB6FF")]
    [InlineData("AccentBar", "#5AA2FF")]
    [InlineData("AccentSoft", "#1E3354")]
    [InlineData("AccentBorder", "#3A6FBF")]
    [InlineData("Success", "#42D392")]
    [InlineData("SuccessBg", "#111A16")]
    [InlineData("SuccessBorder", "#1F4A36")]
    [InlineData("SuccessText2", "#9FD9BC")]
    [InlineData("Warn", "#F2B04A")]
    [InlineData("WarnBg", "#1A1510")]
    [InlineData("WarnBorder", "#5A3A1E")]
    [InlineData("WarnText2", "#E8C891")]
    [InlineData("Danger", "#FF7D6B")]
    [InlineData("DangerText", "#FF9A8A")]
    [InlineData("DangerText2", "#FFB4A6")]
    [InlineData("DiskUsed", "#5D6670")]
    public void Dark_IsTheDesign(string key, string hex) => Assert.Equal(hex, Tokens("Dark.xaml")[key]);

    // README "Design tokeny": Text3 at least 4.5 : 1 on Panel — every text colour on every background it is used on.
    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void Texts_Readable(string file)
    {
        var t = Tokens(file);
        foreach (var fg in new[] { "Text", "Text2", "Text3", "TextSoft", "AccentText" })
            foreach (var bg in new[] { "Bg", "BgHeader", "Panel", "Raised", "Input" })
                Assert.True(Contrast(t[fg], t[bg]) >= 4.5, $"{file}: {fg} on {bg} = {Contrast(t[fg], t[bg]):0.00}");
        foreach (var (fg, bg) in new[] { ("Success", "SuccessBg"), ("SuccessText2", "SuccessBg"), ("SuccessLabel", "SuccessBg"), ("Warn", "WarnBg"),
                     ("WarnText2", "WarnBg"), ("WarnSoftText", "WarnSoftBg"), ("DangerText", "Bg"), ("DangerText", "DangerBg"), ("DangerText2", "WarnListBg"),
                     ("OnAccent", "Accent"), ("OnSuccess", "SuccessFill"), ("OnWarn", "WarnFill"), ("PillRunText", "AccentSoft2"), ("AccentText", "AccentSoft") })
            Assert.True(Contrast(t[fg], t[bg]) >= 4.5, $"{file}: {fg} on {bg} = {Contrast(t[fg], t[bg]):0.00}");
    }

    [Fact]
    public void Choice_WindowsSetting_OverrideWins()
    {
        Assert.Equal(AppTheme.Dark, ThemeChoice.Resolve(null, 0));
        Assert.Equal(AppTheme.Light, ThemeChoice.Resolve(null, 1));
        Assert.Equal(AppTheme.Light, ThemeChoice.Resolve(null, null));      // no value: the Windows default
        Assert.Equal(AppTheme.Light, ThemeChoice.Resolve(null, "0"));       // not a DWORD: ignored
        Assert.Equal(AppTheme.Dark, ThemeChoice.Resolve(" DARK ", 1));
        Assert.Equal(AppTheme.Light, ThemeChoice.Resolve("light", 0));
        Assert.Equal(AppTheme.Dark, ThemeChoice.Resolve("blue", 0));
        Assert.True(ThemeChoice.IsOverride("Dark"));
        Assert.False(ThemeChoice.IsOverride(""));
    }

    // Every dictionary parses, and every style of Offload.xaml finds what it refers to (StaticResource inside it, the
    // icons it merges). Colours are DynamicResource — resolved by the running window.
    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    [InlineData("Icons.xaml")]
    [InlineData("Offload.xaml")]
    public void Dictionary_Loads(string file)
    {
        Exception? error = null;
        int count = 0;
        var t = new Thread(() =>
        {
            try
            {
                var path = Path.Combine(Themes, file);
                var context = new ParserContext { BaseUri = new Uri(path, UriKind.Absolute) };
                using var stream = File.OpenRead(path);
                var dictionary = (ResourceDictionary)XamlReader.Load(stream, context);
                foreach (var key in dictionary.Keys) _ = dictionary[key];   // realises deferred values (and their StaticResources)
                count = dictionary.Keys.Count + dictionary.MergedDictionaries.Sum(m => m.Keys.Count);
                if (file is "Dark.xaml" or "Light.xaml") Assert.IsType<SolidColorBrush>(dictionary["Bg"]);
            }
            catch (Exception e) { error = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        Assert.Null(error);
        Assert.True(count > 10);
    }
}
