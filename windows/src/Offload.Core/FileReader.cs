using Microsoft.Win32.SafeHandles;

namespace Offload.Core;

/// Reads a file past the Windows file cache. Volumes that refuse unbuffered I/O (some network shares)
/// fall back to normal reads instead of failing.
public sealed class FileReader : IDisposable
{
    readonly string path;
    SafeFileHandle handle;

    public bool Unbuffered { get; private set; } = true;

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
            handle = NativeIO.OpenRead(path, unbuffered: false);
            return NativeIO.Read(handle, buffer, offset, path);
        }
    }

    public void Dispose() => handle.Dispose();
}
