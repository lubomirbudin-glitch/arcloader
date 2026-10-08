using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Offload.Core;

/// Thin wrappers over Win32 file I/O. Card reads and disk writes bypass the Windows file cache
/// (FILE_FLAG_NO_BUFFERING), so verification reads the media, never what is still in RAM.
public static unsafe class NativeIO
{
    const FileOptions NoBuffering = (FileOptions)0x20000000;
    public const int ErrorInvalidParameter = 87;

    public static SafeFileHandle OpenRead(string path, bool unbuffered)
    {
        if (unbuffered) unbuffered = ObserveUnbuffered(path, write: false);
        return Wrap("open", path, () => File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            unbuffered ? NoBuffering | FileOptions.SequentialScan : FileOptions.SequentialScan));
    }

    /// Creates or truncates a file. `preallocate` reserves the space up front, so a full disk fails here, not halfway.
    /// Unbuffered = FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH (plan 10, R8): a write is on the disk when it returns
    /// and never in the file cache — what is read later comes from the disk (measured: no slower than without it).
    public static SafeFileHandle OpenWrite(string path, bool unbuffered, long preallocate = 0)
    {
        if (unbuffered) unbuffered = ObserveUnbuffered(path, write: true);
        return Wrap("open", path, () => File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.None,
            unbuffered ? NoBuffering | FileOptions.WriteThrough : FileOptions.None, preallocate));
    }

    /// Tests only (plan 10 data safety, ruling I3): how the "volume" of a test's own folder treats unbuffered opens — every
    /// unbuffered write open is recorded; `RefuseUnbuffered` refuses them with ERROR_INVALID_PARAMETER (some network
    /// shares), `IgnoreUnbuffered` opens them without the flag (a volume that silently ignores it). Keyed by full folder
    /// path, so tests running in parallel never meet.
    internal sealed class TestVolume
    {
        public bool RefuseUnbuffered { get; init; }
        public bool IgnoreUnbuffered { get; init; }
        public System.Collections.Concurrent.ConcurrentQueue<string> Writes { get; } = new();
    }

    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, TestVolume> TestVolumes = new(StringComparer.OrdinalIgnoreCase);

    /// False: open it without FILE_FLAG_NO_BUFFERING after all (a test volume that ignores the flag).
    static bool ObserveUnbuffered(string path, bool write)
    {
        if (TestVolumes.IsEmpty) return true;
        var full = Path.GetFullPath(path);
        foreach (var (folder, volume) in TestVolumes)
        {
            if (!full.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) continue;
            if (write) volume.Writes.Enqueue(full);
            if (volume.RefuseUnbuffered) throw new IOError("open", path, ErrorInvalidParameter);
            if (volume.IgnoreUnbuffered) return false;
        }
        return true;
    }

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
    public static void WriteFileDurably(string path, ReadOnlySpan<byte> data) => WriteViaTemp(path, data, replace: true);

    /// Like WriteFileDurably, but `path` must not exist yet: the rename never replaces (IOError "rename" when it is
    /// there — the existing file stays as it was). For what an append adds next to earlier generations (ruling I1).
    public static void WriteFileNew(string path, ReadOnlySpan<byte> data) => WriteViaTemp(path, data, replace: false);

    static void WriteViaTemp(string path, ReadOnlySpan<byte> data, bool replace)
    {
        var tmp = Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + ".tmp");
        try
        {
            using (var h = OpenWrite(tmp, unbuffered: false))
            {
                if (!data.IsEmpty) Write(h, data, 0, tmp);
                Flush(h, tmp);
            }
            Move(tmp, path, replace);
        }
        catch
        {
            Delete(tmp);
            throw;
        }
    }

    /// Like WriteFileDurably, but an existing `path` is swapped with ReplaceFile: that works while another process (a
    /// backup, an indexer, a reader) holds it open with FileShare.Delete, where a plain rename-over is denied.
    public static void WriteFileReplacing(string path, ReadOnlySpan<byte> data)
    {
        if (!File.Exists(path))
        {
            WriteFileDurably(path, data);
            return;
        }
        var tmp = Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + ".new");
        try
        {
            WriteFileDurably(tmp, data);
            File.Replace(tmp, path, null);
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

    /// "<volume id>|<folder relative to the volume root>", upper-cased — the same for every spelling of a folder (case,
    /// trailing slash, another drive letter or the volume GUID path); "|<full path>" when the volume is unknown. The key
    /// of the append lock and the day-report lock (plan 6 ruling A1, plan 7 ruling M7).
    /// Not the folder's identity: a mapped drive vs its UNC path, a `subst` drive, a junction or an 8.3 short name of
    /// the same folder resolve to another volume or path and give another key — two such spellings are not serialized
    /// (the same gap as AppendLock's; for the day report it goes into the plan 7 docs).
    public static string FolderKey(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd('\\');
        var root = VolumeRoot(full);
        var key = root != null && VolumeId(root) is string volume && full.StartsWith(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            ? volume + "|" + Path.GetRelativePath(root, full)
            : "|" + full;
        return key.ToUpperInvariant();
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

    /// A network volume — a UNC path or a mapped drive (R8: never verified from the proxy's reads).
    public static bool IsRemote(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        // Fix round 1 (M-c): where the folder really is — a junction or a symlink on the way may lead to a share.
        var real = FinalPath(full) ?? full;
        if (real.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        return VolumeRoot(real) is string root && Hardware.UsbNative.GetDriveType(root) == Hardware.UsbNative.DriveRemote;
    }

    /// The path `path` really leads to, junctions and symlinks resolved (GetFinalPathNameByHandle on its nearest existing
    /// folder, the rest appended): "C:\…" or "\\server\share\…"; null when it cannot be told (no drive letter, no access).
    public static string? FinalPath(string path)
    {
        var existing = Path.GetFullPath(path);
        var rest = new List<string>();
        while (!Directory.Exists(existing) && !File.Exists(existing))
        {
            if (Path.GetDirectoryName(existing) is not string parent) return null;
            rest.Insert(0, Path.GetFileName(existing));
            existing = parent;
        }
        using var h = Win32.CreateFile(LongPath(existing), 0, Win32.FILE_SHARE_ALL, 0, Win32.OPEN_EXISTING, Win32.FILE_FLAG_BACKUP_SEMANTICS, 0);
        if (h.IsInvalid) return null;
        return FinalPath(h) is string real ? Path.Combine([real, .. rest]) : null;
    }

    /// The final path of an open file or folder: "C:\…" or "\\server\share\…"; null when it has no drive-letter path.
    public static string? FinalPath(SafeFileHandle h)
    {
        for (uint size = 512; ; )
        {
            var buffer = new char[size];
            uint n;
            fixed (char* p = buffer) n = Win32.GetFinalPathNameByHandle(h, p, size, 0);
            if (n == 0) return null;
            if (n >= size)
            {
                size = n + 1;
                continue;
            }
            var s = new string(buffer, 0, (int)n);
            if (s.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + s[8..];
            return s.StartsWith(@"\\?\", StringComparison.Ordinal) ? s[4..] : s;
        }
    }

    /// Fix round 1 (M-b): the handle really writes past the file cache — the I/O manager kept both NO_BUFFERING and
    /// WRITE_THROUGH on it (FileModeInformation), whatever was asked for. False when it cannot be told.
    public static bool WritesPastTheCache(SafeFileHandle h)
    {
        const uint both = Win32.FILE_WRITE_THROUGH | Win32.FILE_NO_INTERMEDIATE_BUFFERING;
        return Win32.NtQueryInformationFile(h, out _, out uint mode, sizeof(uint), Win32.FileModeInformation) == 0 && (mode & both) == both;
    }

    /// Plan 10 (batch 7–9): the handle really reads past the file cache — the I/O manager kept NO_BUFFERING on it
    /// (FileModeInformation). False when it cannot be told. What verifies a destination's block reads only through such a
    /// handle.
    public static bool ReadsPastTheCache(SafeFileHandle h) =>
        Win32.NtQueryInformationFile(h, out _, out uint mode, sizeof(uint), Win32.FileModeInformation) == 0
        && (mode & Win32.FILE_NO_INTERMEDIATE_BUFFERING) != 0;

    /// The volume serial as the report keeps it (R12): "1A2B-3C4D", the way `vol` prints it; null when unknown.
    public static string? VolumeSerialText(string path) =>
        VolumeSerial(path) is uint s ? $"{s >> 16:X4}-{s & 0xFFFF:X4}" : null;

    public static long? AvailableBytes(string path) =>
        Win32.GetDiskFreeSpaceEx(Path.GetFullPath(path), out var free, out _, out _) ? (long)free : null;

    /// The size of the volume (a card's capacity — part of how history recognises a card, R1).
    public static long? TotalBytes(string path) =>
        Win32.GetDiskFreeSpaceEx(Path.GetFullPath(path), out _, out var total, out _) ? (long)total : null;

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
