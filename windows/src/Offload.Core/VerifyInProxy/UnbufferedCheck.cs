namespace Offload.Core.VerifyInProxy;

/// Spec 8 "Kontrola za běhu" and R8: may this destination be verified from the reads of the proxy (and of the fill-in),
/// or only the classic way? Verify-in-proxy trusts a read only when it provably came from the disk: the copy was written
/// past the file cache (DestWriter.Unbuffered) and the volume honours FILE_FLAG_NO_BUFFERING. Measured on this PC (plan 10
/// prototype, NTFS and exFAT): an unaligned read on a NO_BUFFERING handle is refused with ERROR_INVALID_PARAMETER; a volume
/// that accepts it ignores the flag. Network volumes are never trusted (R8). A speed test (disk speed vs RAM speed, as the
/// spec suggests) is not used: on NVMe the two are too close to tell (2.5 vs 5.7 GB/s measured) — the refusal is exact.
public static class UnbufferedCheck
{
    const int ProbeBytes = 1 << 20;

    /// Null when the destination of `cardPath` passes; else the Czech reason it is verified the classic way (the report
    /// says it per destination).
    public static string? Problem(string cardPath, bool writtenUnbuffered) => Problem(cardPath, writtenUnbuffered, File.GetAttributes);

    /// `attributes`: File.GetAttributes (tests: a compressed or encrypted folder or file without making one); `finalPath`:
    /// NativeIO.FinalPath of the probe's handle (tests: a probe that landed on a share).
    internal static string? Problem(string cardPath, bool writtenUnbuffered, Func<string, FileAttributes> attributes,
        Func<Microsoft.Win32.SafeHandles.SafeFileHandle, string?>? finalPath = null)
    {
        finalPath ??= NativeIO.FinalPath;
        if (!writtenUnbuffered) return Msg.ClassicBufferedWrites;
        if (NativeIO.IsRemote(cardPath)) return Msg.ClassicNetwork;
        var report = Path.Combine(cardPath, Layout.ReportDirName);
        var probe = Path.Combine(report, $".offload-cachecheck-{Environment.ProcessId}-{Guid.NewGuid():N}");
        try
        {
            // Ruling M3: non-cached I/O on an NTFS-compressed or EFS-encrypted stream may still go through the file cache,
            // and the probe (created in the same _REPORT) could pass — such a folder is verified the classic way.
            const FileAttributes cached = FileAttributes.Compressed | FileAttributes.Encrypted;
            if (((attributes(cardPath) | attributes(report)) & cached) != 0) return Msg.ClassicCompressedOrEncrypted;
            using var buffer = new AlignedBuffer(ProbeBytes);
            new Random(ProbeBytes).NextBytes(buffer.Span);
            using (var w = NativeIO.OpenWrite(probe, unbuffered: true, ProbeBytes))
            {
                NativeIO.Write(w, buffer.Span, 0, probe);
                // Fix round 1 (M-c): where the probe really landed — a junction or symlink on the way to a share is a
                // network destination (R8); a path that cannot be told is not trusted either.
                if (finalPath(w) is not string real) return Msg.ClassicCacheCheckFailed("cannot find the real path of the probe");
                if (NativeIO.IsRemote(real)) return Msg.ClassicNetwork;
                // The handle kept NO_BUFFERING | WRITE_THROUGH (fix round 1, M-b).
                if (!NativeIO.WritesPastTheCache(w)) return Msg.ClassicCacheCheck;
            }
            // M3 on the written file itself: it may be compressed or encrypted although its folders do not say so.
            if ((attributes(probe) & cached) != 0) return Msg.ClassicCompressedOrEncrypted;
            using var r = NativeIO.OpenRead(probe, unbuffered: true);
            using var back = new AlignedBuffer(ProbeBytes);
            try
            {
                NativeIO.Read(r, back.Span[..4096], 1, probe);   // unaligned: a volume honouring the flag refuses it
                return Msg.ClassicCacheCheck;
            }
            catch (IOError e) when (e.Code == NativeIO.ErrorInvalidParameter) { }
            int n = NativeIO.Read(r, back.Span, 0, probe);
            return n == ProbeBytes && back.Span.SequenceEqual(buffer.Span) ? null : Msg.ClassicCacheCheck;
        }
        catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException)
        {
            return Msg.ClassicCacheCheckFailed(e.Message);
        }
        finally
        {
            NativeIO.Delete(probe);
        }
    }
}
