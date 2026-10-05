namespace Offload.Core.Hardware;

public enum UsbLinkSpeed { Unknown, Low, Full, High, Super, SuperPlus }

public static class UsbSpeeds
{
    /// The speed the link really runs at. The V2 flags win: a USB 3 device on a USB 3 port still reports
    /// `Speed = High` in CONNECTION_INFORMATION_EX (verified on this PC, WD Elements on port 19).
    public static UsbLinkSpeed Link(byte connectionSpeed, uint? v2Flags)
    {
        uint f = v2Flags ?? 0;
        if ((f & 0b0100) != 0) return UsbLinkSpeed.SuperPlus;
        if ((f & 0b0001) != 0) return UsbLinkSpeed.Super;
        return connectionSpeed switch
        {
            0 => UsbLinkSpeed.Low,
            1 => UsbLinkSpeed.Full,
            2 => UsbLinkSpeed.High,
            3 => UsbLinkSpeed.Super,
            _ => UsbLinkSpeed.Unknown,
        };
    }

    /// What the device could do on a good port and cable — never less than what it runs at.
    /// Without the V2 flags (query unsupported) the descriptor's bcdUSB ≥ 3.0 still says "USB 3 device".
    public static UsbLinkSpeed Capability(uint? v2Flags, ushort bcdUsb, UsbLinkSpeed link)
    {
        uint f = v2Flags ?? 0;
        var cap = (f & 0b1000) != 0 ? UsbLinkSpeed.SuperPlus
            : (f & 0b0010) != 0 ? UsbLinkSpeed.Super
            : v2Flags == null && bcdUsb >= 0x0300 ? UsbLinkSpeed.Super
            : bcdUsb >= 0x0200 ? UsbLinkSpeed.High
            : UsbLinkSpeed.Full;
        return cap > link ? cap : link;
    }

    /// Whether a hub port can do USB 3: from the V2 protocol bits; a link already running at SuperSpeed proves it;
    /// otherwise unknown (null) — never "USB 2.0 only" just because the query is unsupported.
    public static bool? PortIsUsb3(uint? supportedProtocols, UsbLinkSpeed? link)
    {
        if (link >= UsbLinkSpeed.Super) return true;
        return supportedProtocols is uint p ? (p & 0b100) != 0 : null;
    }

    public static string Label(UsbLinkSpeed s) => s switch
    {
        UsbLinkSpeed.Low => "USB 1.0 (1,5 Mb/s)",
        UsbLinkSpeed.Full => "USB 1.1 (12 Mb/s)",
        UsbLinkSpeed.High => "USB 2.0 (480 Mb/s)",
        UsbLinkSpeed.Super => "USB 3 (5 Gb/s)",
        UsbLinkSpeed.SuperPlus => "USB 3 (10 Gb/s a víc)",
        _ => "neznámá",
    };

    /// Realistic ceiling for a disk or card reader over this link, MB/s (protocol overhead included).
    public static double PracticalMBps(UsbLinkSpeed s) => s switch
    {
        UsbLinkSpeed.Low => 0.15,
        UsbLinkSpeed.Full => 1,
        UsbLinkSpeed.High => 40,
        UsbLinkSpeed.Super => 450,
        UsbLinkSpeed.SuperPlus => 1000,
        _ => 0,
    };
}
