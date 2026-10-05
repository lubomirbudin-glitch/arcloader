using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public class UsbSpeedsTests
{
    // Review focus 1: a USB 3 device on a USB 3 port reports Speed=High; the V2 flag is the truth.
    [Fact]
    public void Link_V2FlagWinsOverSpeedField()
    {
        Assert.Equal(UsbLinkSpeed.Super, UsbSpeeds.Link(2, 0b0011));
        Assert.Equal(UsbLinkSpeed.SuperPlus, UsbSpeeds.Link(2, 0b1111));
        Assert.Equal(UsbLinkSpeed.High, UsbSpeeds.Link(2, 0b0010));
        Assert.Equal(UsbLinkSpeed.Full, UsbSpeeds.Link(1, 0));
        Assert.Equal(UsbLinkSpeed.Low, UsbSpeeds.Link(0, null));
        Assert.Equal(UsbLinkSpeed.Super, UsbSpeeds.Link(3, null));
        Assert.Equal(UsbLinkSpeed.Unknown, UsbSpeeds.Link(9, null));
    }

    [Fact]
    public void Capability_FromFlagsElseDescriptor()
    {
        Assert.Equal(UsbLinkSpeed.Super, UsbSpeeds.Capability(0b0010, 0x0210, UsbLinkSpeed.High));
        Assert.Equal(UsbLinkSpeed.SuperPlus, UsbSpeeds.Capability(0b1010, 0x0320, UsbLinkSpeed.Super));
        Assert.Equal(UsbLinkSpeed.High, UsbSpeeds.Capability(0, 0x0200, UsbLinkSpeed.High));
        Assert.Equal(UsbLinkSpeed.Full, UsbSpeeds.Capability(null, 0x0110, UsbLinkSpeed.Full));
        // Never below what the link actually runs at.
        Assert.Equal(UsbLinkSpeed.Super, UsbSpeeds.Capability(null, 0x0200, UsbLinkSpeed.Super));
    }

    [Fact]
    public void LabelsAndCeilings()
    {
        Assert.Equal("USB 2.0 (480 Mb/s)", UsbSpeeds.Label(UsbLinkSpeed.High));
        Assert.Equal("USB 3 (5 Gb/s)", UsbSpeeds.Label(UsbLinkSpeed.Super));
        Assert.Equal("USB 3 (10 Gb/s a víc)", UsbSpeeds.Label(UsbLinkSpeed.SuperPlus));
        Assert.Equal("neznámá", UsbSpeeds.Label(UsbLinkSpeed.Unknown));
        Assert.Equal(40, UsbSpeeds.PracticalMBps(UsbLinkSpeed.High));
        Assert.Equal(450, UsbSpeeds.PracticalMBps(UsbLinkSpeed.Super));
        Assert.Equal(1000, UsbSpeeds.PracticalMBps(UsbLinkSpeed.SuperPlus));
        Assert.Equal(0, UsbSpeeds.PracticalMBps(UsbLinkSpeed.Unknown));
    }
}
