using Offload.Core.Desktop;
using Offload.Core.Hardware;
using Offload.Core.History;
using Offload.Core.Space;

namespace Offload.Core.Tests;

/// UI návrh, nová věc 6: the camera label of a card and how cards and disks are connected; the destination tile.
public class CardFactsTests
{
    static readonly LutCatalog Cat = LutCatalog.Load(LutCatalog.DefaultFolder);

    static CardInsight Insight(params Clip[] clips) => new() { Source = @"E:\", Clips = clips };

    static Clip Video(string path, bool proxy = false) =>
        new() { Name = Path.GetFileNameWithoutExtension(path), Path = path, Format = ClipFormat.Video, Segments = [path], IsCameraProxy = proxy };

    [Fact]
    public void Label_ModelAndCurve()
    {
        Assert.Equal("ALEXA 35 · LogC4", new CameraLabel("ALEXA 35", "LogC4").Text);
        Assert.Equal("ALEXA 35", new CameraLabel("ALEXA 35", null).Text);
        Assert.Equal("S-Log3", new CameraLabel(null, "S-Log3").Text);
        Assert.Null(new CameraLabel(" ", null).Text);
        Assert.Equal("ALEXA 35", CameraLabel.Model("ARRI ALEXA 35"));
        Assert.Equal("ILME-FX6V", CameraLabel.Model("Sony ILME-FX6V"));
        Assert.Equal("RED", CameraLabel.Model("RED"));
        Assert.Equal(["LogC4", "S-Log3", "Film Gen 5", "C-Log3", "Log3G10"],
            new[] { "ARRI LogC4", "S-Log3", "Blackmagic Film Generation 5", "Canon Log 3", "Log3G10" }.Select(CameraLabel.ShortCurve));
    }

    [Fact]
    public void Hints_ArriFromFfprobe_RawByFormat_ProxySkipped()
    {
        var arri = Insight(Video("SUB/A035C001.mp4", proxy: true), Video("A035C001_261007_R2KQ.mxf"));
        string? asked = null;
        var label = CameraHints.Probe(arri, new ExternalTools(), Cat, CancellationToken.None, p => { asked = p; return new ClipInfo { Camera = "ARRI ALEXA 35" }; });
        Assert.Equal("ALEXA 35 · LogC4", label!.Text);
        Assert.Equal(@"E:\A035C001_261007_R2KQ.mxf", asked);   // the first clip that is not a camera proxy

        var braw = Insight(new Clip { Name = "A001", Path = "A001.braw", Format = ClipFormat.Braw, Segments = ["A001.braw"] });
        Assert.Equal("Blackmagic RAW", CameraHints.Probe(braw, new ExternalTools(), Cat, CancellationToken.None, _ => throw new InvalidOperationException())!.Text);
    }

    [Fact]
    public void Hints_UnknownOrUnreadable_NoLabel()
    {
        var clip = Insight(Video("C0001.MP4"));
        Assert.Null(CameraHints.Probe(clip, new ExternalTools(), Cat, CancellationToken.None, _ => throw new ToolError("ffprobe", -1, "did not respond within 5 s — terminated")));
        Assert.Null(CameraHints.Probe(clip, new ExternalTools(), Cat, CancellationToken.None, _ => new ClipInfo()));
        Assert.Null(CameraHints.Probe(Insight(), new ExternalTools(), Cat, CancellationToken.None, _ => new ClipInfo { Camera = "X" }));
        Assert.Null(CameraHints.Probe(new CardInsight { Source = @"E:\", Error = "x", Clips = [Video("a.mov")] }, new ExternalTools(), Cat, CancellationToken.None,
            _ => new ClipInfo { Camera = "X" }));
        Assert.Equal("FX6", CameraHints.Probe(clip, new ExternalTools(), Cat, CancellationToken.None, _ => new ClipInfo { Camera = "FX6" })!.Text);
    }

    static DriveLink Usb(UsbLinkSpeed speed) =>
        new("E:\\", StorageBus.Usb, "reader", new UsbLink("PCI\\HC", "x", "R#5", 5, 5, speed, speed, true, false));

    [Fact]
    public void Connection_InAWordOrTwo()
    {
        Assert.Equal("reader USB 3 · 5 Gb/s", ConnectionLabel.Short(Usb(UsbLinkSpeed.Super), card: true));
        Assert.Equal("USB 3 · 10 Gb/s", ConnectionLabel.Short(Usb(UsbLinkSpeed.SuperPlus), card: false));
        Assert.Equal("USB 2.0", ConnectionLabel.Short(Usb(UsbLinkSpeed.High), card: false));
        Assert.Equal("SD reader", ConnectionLabel.Short(new DriveLink("E:\\", StorageBus.Sd, "", null), card: true));
        Assert.Equal("NVMe", ConnectionLabel.Short(new DriveLink("D:\\", StorageBus.Nvme, "", null), card: false));
        Assert.Equal("network", ConnectionLabel.Short(new DriveLink("Z:\\", StorageBus.Network, "", null), card: false));
        Assert.Null(ConnectionLabel.Short(new DriveLink("D:\\", StorageBus.Unknown, "", null), card: false));
    }

    [Fact]
    public void DestinationTile_TextsAndBar()
    {
        Assert.Equal("7.20 TB free of 8 TB · NTFS · NVMe", DestinationTile.FreeText(7_200_000_000_000, 8_000_000_000_000, "NTFS", "NVMe"));
        Assert.Equal("1.92 TB free · exFAT", DestinationTile.FreeText(1_920_000_000_000, null, "exFAT", null));
        Assert.Equal("7.20 TB free", DestinationTile.CompactFree(7_200_000_000_000));
        static (double, double) Bar(long total, long free, long need) =>
            DestinationTile.Bar(total, free, need) is var b ? (Math.Round(b.Used, 3), Math.Round(b.Need, 3)) : default;
        Assert.Equal((0.1, 0.02), Bar(8_000_000_000_000, 7_200_000_000_000, 160_000_000_000));
        Assert.Equal((0.5, 0.01), Bar(1000, 500, 1));            // a tiny need still shows
        Assert.Equal((0.9, 0.1), Bar(1000, 100, 5000));          // never past the free space
        Assert.Equal((0.0, 0.0), Bar(0, 0, 10));
        VolumeDemand D(string v, long n) => new(v, "x", new SpaceNeed(n, 0, 0));
        Assert.Equal(30, DestinationTile.Need([D("A", 10), D("a", 20), D("B", 99)], "A"));
        var problems = new List<SpaceProblem>
        {
            new(SpaceSeverity.Warning, "RAID_A (D:)", SpaceNeed.None, 0, "w"),
            new(SpaceSeverity.Blocking, "RAID_A (D:), SHUTTLE_B (D:)", SpaceNeed.None, 0, "b"),
        };
        Assert.Equal(SpaceSeverity.Blocking, DestinationTile.Severity(problems, "SHUTTLE_B (D:)"));
        Assert.Equal(SpaceSeverity.Blocking, DestinationTile.Severity(problems, "RAID_A (D:)"));
        Assert.Null(DestinationTile.Severity(problems, "OTHER (E:)"));
    }
}
