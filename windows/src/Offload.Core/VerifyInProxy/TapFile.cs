using System.Collections.Concurrent;
using Microsoft.Win32.SafeHandles;

namespace Offload.Core.VerifyInProxy;

/// A file on a destination read in whole blocks straight from the disk (FILE_FLAG_NO_BUFFERING, spec 8) — never from the
/// file cache, so what it gives is what the disk holds. A volume that refuses unbuffered reads is not used (IOError with
/// ERROR_INVALID_PARAMETER from the constructor): its copies are verified the classic way instead. Batch 7–9 belts: the
/// handle must really have kept NO_BUFFERING (a volume may silently drop it), and an NTFS-compressed or EFS-encrypted
/// file is refused (its non-cached reads may still be served through the cache, preflight M3) — the same IOError.
internal sealed class DiskBlocks : IDisposable
{
    /// What one disk read asks for when the caller wants to hear about progress (the tap: the stall watchdog sees a slow
    /// but working disk as alive, piece by piece, not only after a whole 16 MiB block).
    public const int PieceBytes = 1 << 20;

    /// Tests: called before every disk read of the file at the key (its full path) — a disk that is slow or hangs.
    internal static readonly ConcurrentDictionary<string, Action> ReadHooks = new(StringComparer.OrdinalIgnoreCase);

    readonly SafeFileHandle handle;

    public string Path { get; }
    /// The file's length on the disk now.
    public long Length { get; }

    public DiskBlocks(string path)
    {
        Path = path;
        handle = NativeIO.OpenRead(path, unbuffered: true);
        try
        {
            if (!NativeIO.ReadsPastTheCache(handle)
                || (File.GetAttributes(handle) & (FileAttributes.Compressed | FileAttributes.Encrypted)) != 0)
                throw new IOError("open", path, NativeIO.ErrorInvalidParameter);
            Length = RandomAccess.GetLength(handle);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            handle.Dispose();
            throw new IOError("open", path, e);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// The block at `offset` (a multiple of the sector size — every block offset is) into `buffer`; the bytes read, fewer
    /// only at the end of the file. The whole block is asked for (an unbuffered read must be whole sectors): in one read,
    /// or with `piece` in reads of PieceBytes, each reported once it is in.
    public int Read(long offset, AlignedBuffer buffer, int blockSize, Action<int>? piece = null)
    {
        if (piece == null)
        {
            Hook();
            return NativeIO.Read(handle, buffer.Span[..blockSize], offset, Path);
        }
        int done = 0;
        while (done < blockSize)
        {
            int want = Math.Min(PieceBytes, blockSize - done);
            Hook();
            int n = NativeIO.Read(handle, buffer.Span.Slice(done, want), offset + done, Path);
            if (n <= 0) break;
            done += n;
            piece(n);
            if (n < want) break;   // the end of the file
        }
        return done;
    }

    void Hook()
    {
        if (!ReadHooks.IsEmpty && ReadHooks.TryGetValue(System.IO.Path.GetFullPath(Path), out var hook)) hook();
    }

    /// A read under way keeps the handle (SafeHandle) until it returns; the next one is refused.
    public void Dispose() => handle.Dispose();
}

/// Mode A's source (R3): the primary destination's copy of a clip, read in whole blocks straight from the disk; each
/// block read is handed to the verifier once, then served to ffmpeg from a small cache (a seek back to a block just
/// read — moov at the end, an MXF footer — costs no second read). A block that fell out of the cache and is read again
/// goes to the verifier again (a different answer is a mismatch, never hidden). Thread-safe (ffmpeg may open a second
/// connection before it closes the first). Activity (for the proxy's stall watchdog) rises with every piece read from
/// the disk. After Dispose (the lease ended) nothing is read any more; Dispose never waits for a read under way (final
/// review, item 4: a hung disk would hold the job thread) — the reader frees the cached blocks once its read returns.
public sealed class TapFile : ITapSource, IDisposable
{
    public const int CacheBlocks = 4;

    readonly DiskBlocks disk;
    readonly BlockVerifier verifier;
    readonly Lock gate = new();
    readonly List<(int Block, AlignedBuffer Buffer, int Length)> cache = [];
    long readFromDisk;
    volatile bool disposed;

    public long Length => disk.Length;
    /// Bytes read from the disk so far (every block once, unless it fell out of the cache).
    public long ReadFromDisk => Interlocked.Read(ref readFromDisk);
    long ITapSource.Activity => ReadFromDisk;

    /// Opens `path` unbuffered (IOError when the volume refuses it) and tells the verifier its length.
    public TapFile(string path, BlockVerifier verifier)
    {
        this.verifier = verifier;
        disk = new DiskBlocks(path);
        verifier.ObserveLength(disk.Length);
    }

    public int Read(long offset, Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (offset >= disk.Length || buffer.IsEmpty) return 0;
        int size = verifier.Manifest.BlockSize;
        int done = 0;
        try
        {
            lock (gate)
            {
                while (done < buffer.Length && offset + done < disk.Length)
                {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    long at = offset + done;
                    int b = (int)(at / size);
                    var (block, length) = Block(b, size);
                    int from = (int)(at - (long)b * size);
                    if (from >= length) break;   // the disk gave less than the file's length (it shrank meanwhile)
                    int n = Math.Min(length - from, buffer.Length - done);
                    block.Span.Slice(from, n).CopyTo(buffer[done..]);
                    done += n;
                }
            }
        }
        finally
        {
            FreeIfDisposed();   // Dispose came while this read held the blocks
        }
        return done;
    }

    (AlignedBuffer Buffer, int Length) Block(int b, int size)
    {
        int hit = cache.FindIndex(c => c.Block == b);
        if (hit >= 0)
        {
            var c = cache[hit];
            cache.RemoveAt(hit);
            cache.Add(c);   // most recently used last
            return (c.Buffer, c.Length);
        }
        AlignedBuffer buffer;
        if (cache.Count >= CacheBlocks)
        {
            buffer = cache[0].Buffer;
            cache.RemoveAt(0);
        }
        else buffer = new AlignedBuffer(size);
        int n;
        try
        {
            n = disk.Read((long)b * size, buffer, size, piece =>
            {
                Interlocked.Add(ref readFromDisk, piece);
                ObjectDisposedException.ThrowIf(disposed, this);   // the lease ended: stop after this piece
            });
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
        // Only a whole read reaches the verifier: every byte of the block (or of the shorter last one) is in `buffer`.
        verifier.Feed((long)b * size, buffer.Span[..n]);
        cache.Add((b, buffer, n));
        return (buffer, n);
    }

    /// Never waits for a read under way (it may hang on the disk): the blocks are freed now when no read holds them,
    /// else by the last read when it returns (FreeIfDisposed after it lets go). The handle closes once no read uses it.
    public void Dispose()
    {
        disposed = true;
        FreeIfDisposed();
        disk.Dispose();
    }

    /// After `disposed`: whoever finds the lock free frees the cached blocks — every holder checks once it lets go, so
    /// the last one always does.
    void FreeIfDisposed()
    {
        if (!disposed || !gate.TryEnter()) return;
        try
        {
            foreach (var c in cache) c.Buffer.Dispose();
            cache.Clear();
        }
        finally
        {
            gate.Exit();
        }
    }
}
