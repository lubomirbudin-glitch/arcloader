using Microsoft.Win32.SafeHandles;

namespace Offload.Core;

/// Reads a file past the Windows file cache. Volumes that refuse unbuffered I/O (some network shares)
/// fall back to normal reads instead of failing.
public sealed class FileReader : IDisposable
{
    readonly string path;
    SafeFileHandle handle;

    public bool Unbuffered { get; private set; } = true;

    /// Plan 10 (ruling I3): every read so far really came from the disk — the handle kept FILE_FLAG_NO_BUFFERING (a volume
    /// may drop it silently), the file is not NTFS-compressed or EFS-encrypted (their non-cached reads may still be
    /// served through the cache) and no read fell back to the cache. False = what was read may have come from memory.
    public bool FromDisk { get; private set; }

    public FileReader(string path)
    {
        this.path = path;
        try
        {
            handle = NativeIO.OpenRead(path, unbuffered: true);
        }
        catch (IOError e) when (e.Code == NativeIO.ErrorInvalidParameter)
        {
            Unbuffered = false;
            handle = NativeIO.OpenRead(path, unbuffered: false);
        }
        FromDisk = Unbuffered && ReadsFromDisk(handle);
    }

    static bool ReadsFromDisk(SafeFileHandle h)
    {
        try
        {
            return NativeIO.ReadsPastTheCache(h) && (File.GetAttributes(h) & (FileAttributes.Compressed | FileAttributes.Encrypted)) == 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// The file's length now.
    public long Length
    {
        get
        {
            try { return RandomAccess.GetLength(handle); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new IOError("read", path, e); }
        }
    }

    /// One read at `offset` (a multiple of the buffer size); fewer bytes than asked only at end of file.
    public int Read(Span<byte> buffer, long offset)
    {
        try
        {
            return NativeIO.Read(handle, buffer, offset, path);
        }
        catch (IOError e) when (Unbuffered && e.Code == NativeIO.ErrorInvalidParameter)
        {
            handle.Dispose();
            Unbuffered = false;
            FromDisk = false;
            handle = NativeIO.OpenRead(path, unbuffered: false);
            return NativeIO.Read(handle, buffer, offset, path);
        }
    }

    public void Dispose() => handle.Dispose();
}
