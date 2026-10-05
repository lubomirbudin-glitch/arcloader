namespace Offload.Core.Desktop;

/// Which destination rows the GUI's 5 s health check probes again (off the UI thread, one probe per row at a time).
public static class DestinationHealth
{
    /// A disk that can vanish without Windows telling the volume scan: a network share (a NAS) or removable media.
    /// USB hard disks are "fixed" and arrive/leave through the volume scan, so they are not polled.
    /// `type`: the drive type of the root's letter, null when unknown.
    public static bool MayDropOut(string root, DriveType? type) =>
        root.StartsWith(@"\\", StringComparison.Ordinal) || type is DriveType.Network or DriveType.Removable;

    /// A missing destination is always probed again (it may come back); a present one only when it may drop out.
    /// Never while the row's first probe is still running.
    public static bool ShouldReprobe(bool checking, bool exists, bool mayDropOut) => !checking && (!exists || mayDropOut);
}
