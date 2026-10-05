using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Offload.Core;

/// Thin wrappers over Win32 file I/O. Card reads and disk writes bypass the Windows file cache
/// (FILE_FLAG_NO_BUFFERING), so verification reads the media, never what is still in RAM.
public static unsafe class NativeIO
{
    const FileOptions NoBuffering = (FileOptions)0x20000000;
    public const int ErrorInvalidParameter = 87;

    public static SafeFileHandle OpenRead(string path, bool unbuffered) =>
        Wrap("open", path, () => File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            unbuffered ? NoBuffering | FileOptions.SequentialScan : FileOptions.SequentialScan));

    /// Creates or truncates a file. `preallocate` reserves the space up front, so a full disk fails here, not halfway.
    public static SafeFileHandle OpenWrite(string path, bool unbuffered, long preallocate = 0) =>
        Wrap("open", path, () => File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.None,
            unbuffered ? NoBuffering : FileOptions.None, preallocate));

    public static SafeFileHandle OpenExisting(string path) =>
        Wrap("open", path, () => File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.None));

    /// One read at `offset`; fewer bytes than asked only at end of file.
    public static int Read(SafeFileHandle h, Span<byte> buffer, long offset, string path)
    {
        try { return RandomAccess.Read(h, buffer, offset); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new IOError("read", path, e); }
    }

    public static void Write(SafeFileHandle h, ReadOnlySpan<byte> data, long offset, string path)
    {
        try { RandomAccess.Write(h, data, offset); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new IOError("write", path, e); }
    }

    public static void SetLength(SafeFileHandle h, long length, string path) =>
        Wrap("truncate", path, () => { RandomAccess.SetLength(h, length); return 0; });

    /// Writes the file out and makes the drive empty its own write cache (the F_FULLFSYNC of Windows).
    public static void Flush(SafeFileHandle h, string path)
    {
        if (!Win32.FlushFileBuffers(h)) throw new IOError("flush", path, Marshal.GetLastPInvokeError());
    }

    public static void SetModified(SafeFileHandle h, DateTime utc, string path) =>
        Wrap("settime", path, () => { File.SetLastWriteTimeUtc(h, utc); return 0; });

    public static void Move(string from, string to, bool replace)
    {
        uint flags = Win32.MOVEFILE_WRITE_THROUGH | (replace ? Win32.MOVEFILE_REPLACE_EXISTING : 0);
        if (!Win32.MoveFileEx(LongPath(from), LongPath(to), flags)) throw new IOError("rename", to, Marshal.GetLastPInvokeError());
    }

    /// Best effort: used for cleanup, never fails.
    public static void Delete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// Write + flush via a temp name, so a crash never leaves a half-written report.
    public static void WriteFileDurably(string path, ReadOnlySpan<byte> data)
    {
        var tmp = Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + ".tmp");
        try
        {
            using (var h = OpenWrite(tmp, unbuffered: false))
            {
                if (!data.IsEmpty) Write(h, data, 0, tmp);
                Flush(h, tmp);
            }
            Move(tmp, path, replace: true);
        }
        catch
        {
            Delete(tmp);
            throw;
        }
    }

    public static void Mkdirs(string path) => Wrap("mkdir", path, () => Directory.CreateDirectory(path));

    public static string? VolumeRoot(string path)
    {
        char* buf = stackalloc char[1024];
        return Win32.GetVolumePathName(Path.GetFullPath(path), buf, 1024) ? new string(buf) : null;
    }

    /// Identity of the volume a path is on: the volume GUID path, or the share root for network paths.
    public static string? VolumeId(string path)
    {
        var root = VolumeRoot(path);
        if (root == null) return null;
        char* buf = stackalloc char[64];
        return Win32.GetVolumeNameForVolumeMountPoint(root, buf, 64) ? new string(buf) : root.ToLowerInvariant();
    }

    /// The serial number the file system got when it was formatted, or null (no volume there).
    public static uint? VolumeSerial(string path)
    {
        var root = VolumeRoot(path);
        if (root == null) return null;
        return Win32.GetVolumeInformation(root, null, 0, out var serial, out _, out _, null, 0) ? serial : null;
    }

    /// "NTFS", "exFAT", "FAT32", "ReFS" … or "?".
    public static string FilesystemType(string path)
    {
        var root = VolumeRoot(path);
        if (root == null) return "?";
        char* fs = stackalloc char[64];
        return Win32.GetVolumeInformation(root, null, 0, out _, out _, out _, fs, 64) ? new string(fs) : "?";
    }

    public static string? VolumeLabel(string path)
    {
        var root = VolumeRoot(path);
        if (root == null) return null;
        char* name = stackalloc char[261];
        return Win32.GetVolumeInformation(root, name, 261, out _, out _, out _, null, 0) ? new string(name) : null;
    }

    public static long? AvailableBytes(string path) =>
        Win32.GetDiskFreeSpaceEx(Path.GetFullPath(path), out var free, out _, out _) ? (long)free : null;

    /// Write size unit for unbuffered I/O on this file's volume (at least 4096).
    public static int SectorSize(SafeFileHandle h)
    {
        Win32.FILE_STORAGE_INFO info;
        if (!Win32.GetFileInformationByHandleEx(h, Win32.FileStorageInfo, &info, (uint)sizeof(Win32.FILE_STORAGE_INFO)))
            return AlignedBuffer.Alignment;
        return (int)Math.Max(AlignedBuffer.Alignment, Math.Max(info.LogicalBytesPerSector, info.PhysicalBytesPerSectorForPerformance));
    }

    /// Keeps the system from sleeping while the returned object lives. Dispose on the same thread.
    public static IDisposable KeepAwake()
    {
        Win32.SetThreadExecutionState(Win32.ES_CONTINUOUS | Win32.ES_SYSTEM_REQUIRED);
        return new AwakeToken();
    }

    sealed class AwakeToken : IDisposable
    {
        public void Dispose() => Win32.SetThreadExecutionState(Win32.ES_CONTINUOUS);
    }

    /// Win32 calls (unlike .NET APIs) need the \\?\ prefix for paths over 260 characters.
    internal static string LongPath(string p)
    {
        var full = Path.GetFullPath(p);
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal)) return full;
        return full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
    }

    static T Wrap<T>(string op, string path, Func<T> f)
    {
        try { return f(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new IOError(op, path, e); }
    }
}
