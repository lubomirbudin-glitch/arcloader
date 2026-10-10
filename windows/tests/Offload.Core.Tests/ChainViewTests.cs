using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public class ChainViewTests
{
    const string Hc = "Intel xHCI";

    static RoleLink Usb(string role, string product, UsbLinkSpeed link, string controllerId = "PCI\\HC") =>
        new(role, new DriveLink("x:\\", StorageBus.Usb, product, new UsbLink(controllerId, Hc, "R#5", 5, 5, link, link, true, false)));

    static RoleLink Sata(string role) => new(role, new DriveLink("D:\\", StorageBus.Sata, "ST1000", null));

    [Fact]
    public void Build_UtilizationAndConnection()
    {
        var c = ChainView.Build([Usb("Card", "reader", UsbLinkSpeed.Super), Sata("Destination 1")], [225, 98]);
        Assert.Equal(0.5, c[0].Utilization!.Value, 2);
        Assert.Equal("USB 3 (5 Gb/s), port 5", c[0].Connection);
        Assert.Null(c[1].Utilization);
        Assert.Equal("SATA", c[1].Connection);
    }

}

public class BrakeMeterTests
{
    const long Chunk = 16 << 20;

    // Feeds a copy that moves `rate` bytes/s through a lockstep engine with a fixed backlog per writer.
    static Brake? Run(BrakeMeter m, double seconds, long rate, params long[] lagChunks)
    {
        Brake? last = null;
        for (double t = 0; t <= seconds + 1e-9; t += 0.5)
        {
            long read = (long)(rate * t) + lagChunks.Max() * Chunk;
            // Quantised by whole chunks, like DestWriter: the writer reports only finished 16 MB blocks.
            last = m.Add(t, read, lagChunks.Select(l => (read - l * Chunk) / Chunk * Chunk).ToArray());
        }
        return last;
    }

    [Fact]
    public void LockstepEqualRates_LaggingWriterBrakes()
    {
        var b = Run(new BrakeMeter(Chunk, 16), 6, 57_000_000, 0, 15);
        Assert.Equal(2, b!.Value.Element);
        Assert.InRange(b.Value.MBps, 50, 65);
    }

    [Fact]
    public void AllLagsSmall_CardBrakes()
    {
        var b = Run(new BrakeMeter(Chunk, 16), 6, 300_000_000, 1, 0, 1);
        Assert.Equal(0, b!.Value.Element);
    }

    [Fact]
    public void LaggingWriterBeatsTie_FirstOfEqualLags()
    {
        // Same rates everywhere (a "tie" by speed): the backlog still decides, and never for the card.
        var b = Run(new BrakeMeter(Chunk, 16), 6, 100_000_000, 14, 14);
        Assert.Equal(1, b!.Value.Element);
        b = Run(new BrakeMeter(Chunk, 16), 6, 100_000_000, 3, 0);
        Assert.Equal(1, b!.Value.Element);
    }

    // At the start the pool is still filling: the slow writer's backlog grows before it is large on average.
    [Fact]
    public void GrowingBacklog_WriterBrakesBeforeThePoolIsFull()
    {
        var m = new BrakeMeter(Chunk, 16);
        Brake? b = null;
        for (double t = 0; t <= 2.0 + 1e-9; t += 0.5)
        {
            long read = (long)(100_000_000 * t), written = (long)(60_000_000 * t);   // card 100 MB/s, disk 60 MB/s
            b = m.Add(t, read, [written]);
        }
        Assert.Equal(1, b!.Value.Element);
    }

    [Fact]
    public void SmallPool_ThresholdIsHalfThePool()
    {
        Assert.Equal(1, Run(new BrakeMeter(Chunk, 2), 6, 50_000_000, 1)!.Value.Element);
        Assert.Equal(0, Run(new BrakeMeter(Chunk, 16), 6, 50_000_000, 1)!.Value.Element);
    }

    [Fact]
    public void LinkFull_FromTheBrakingElementsLink()
    {
        var b = Run(new BrakeMeter(Chunk, 16, [450, 40]), 6, 38_000_000, 15);
        Assert.Equal(1, b!.Value.Element);
        Assert.True(b.Value.LinkFull);
        b = Run(new BrakeMeter(Chunk, 16, [450, null]), 6, 38_000_000, 15);
        Assert.False(b!.Value.LinkFull);
        Assert.Null(b.Value.Utilization);
    }

    [Fact]
    public void NoVerdictUntilHalfTheWindow_AndSmoothedOverIt()
    {
        var m = new BrakeMeter(Chunk, 16, windowSeconds: 4);
        Assert.Null(m.Add(0, 0, [0]));
        Assert.Null(m.Add(1, 100 * Chunk, [100 * Chunk - 15 * Chunk]));
        Assert.Equal(1, m.Add(2, 200 * Chunk, [200 * Chunk - 15 * Chunk])!.Value.Element);
        // One momentary catch-up of the writer does not flip the verdict.
        Assert.Equal(1, m.Add(2.5, 210 * Chunk, [210 * Chunk])!.Value.Element);
    }
}
