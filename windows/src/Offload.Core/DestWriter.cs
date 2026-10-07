using Microsoft.Win32.SafeHandles;

namespace Offload.Core;

/// Writes the stream to one destination. Each destination has its own thread, so disks run in parallel.
internal sealed class DestWriter : IDisposable
{
    readonly SerialWorker worker;
    readonly OffloadOptions options;
    readonly int index;
    SafeFileHandle? handle;
    string partPath = "";
    string? currentError;
    int align = AlignedBuffer.Alignment;
    long written;
    long verified;

    public string CardPath { get; }
    public DestFileResult[] Results { get; }
    /// False once this volume refused unbuffered writes and the writer fell back to the file cache.
    public bool Unbuffered { get; private set; }
    public long Written => Interlocked.Read(ref written);
    public long Verified => Interlocked.Read(ref verified);

    public DestWriter(int index, string cardPath, int fileCount, OffloadOptions options)
    {
        CardPath = cardPath;
        this.index = index;
        this.options = options;
        Unbuffered = !options.ForceBuffered;
        Results = new DestFileResult[fileCount];
        worker = new SerialWorker($"offload.dest{index}");
    }

    /// In-progress name: ".<name>.offload-part" next to the final file, so an unfinished copy never looks finished.
    public static string PartName(string rel)
    {
        int slash = rel.LastIndexOf('/');
        var name = "." + rel[(slash + 1)..] + ".offload-part";
        return slash < 0 ? name : rel[..slash] + "/" + name;
    }

    public void Begin(SourceFile file) => worker.Post(() =>
    {
        currentError = null;
        partPath = Layout.Join(CardPath, PartName(file.RelPath));
        try
        {
            handle = OpenPart(file.Size);
            File.SetAttributes(partPath, FileAttributes.Hidden);
        }
        catch (Exception e)
        {
            currentError = e.Message;
            CloseAndRemovePart();
        }
    });

    SafeFileHandle OpenPart(long size)
    {
        if (Unbuffered)
        {
            SafeFileHandle h;
            try
            {
                h = NativeIO.OpenWrite(partPath, unbuffered: true, size);
            }
            catch (IOError e) when (e.Code == NativeIO.ErrorInvalidParameter)
            {
                Unbuffered = false;
                return NativeIO.OpenWrite(partPath, unbuffered: false, size);
            }
            align = NativeIO.SectorSize(h);
            if (options.ChunkSize % align == 0) return h;
            h.Dispose();   // sector larger than the block size: unbuffered writes are impossible here
            Unbuffered = false;
        }
        return NativeIO.OpenWrite(partPath, unbuffered: false, size);
    }

    public void Write(Chunk c) => worker.Post(() =>
    {
        try
        {
            if (handle == null) return;
            options.BeforeDestWrite?.Invoke(index);
            // Unbuffered writes must be whole sectors: the last block is written padded, Finish trims the file.
            int len = Unbuffered ? (c.Length + align - 1) / align * align : c.Length;
            try
            {
                NativeIO.Write(handle, c.Buffer.Span[..len], c.Offset, partPath);
            }
            catch (IOError e) when (Unbuffered && e.Code == NativeIO.ErrorInvalidParameter)
            {
                SwitchToBuffered();
                NativeIO.Write(handle!, c.Buffer.Span[..c.Length], c.Offset, partPath);
            }
            Interlocked.Add(ref written, c.Length);
        }
        catch (Exception e)
        {
            currentError = e.Message;
            CloseAndRemovePart();
        }
        finally
        {
            c.Release();
        }
    });

    void SwitchToBuffered()
    {
        handle?.Dispose();
        Unbuffered = false;
        handle = NativeIO.OpenExisting(partPath);
    }

    public void Finish(int index, SourceFile file) => worker.Post(() =>
    {
        if (handle == null || currentError != null)
        {
            Results[index] = new DestFileResult(FileState.Failed, currentError ?? "write failed");
            CloseAndRemovePart();
            return;
        }
        var finalPath = Layout.Join(CardPath, file.RelPath);
        try
        {
            NativeIO.SetLength(handle, file.Size, partPath);
            // Big files are flushed one by one, small ones together in FlushDevice. Without unbuffered
            // writes the data may still sit in the file cache, so then every file is flushed.
            if (file.Size >= options.FullSyncThreshold || !Unbuffered) NativeIO.Flush(handle, partPath);
            NativeIO.SetModified(handle, file.ModifiedUtc, partPath);
            handle.Dispose();
            handle = null;
            NativeIO.Move(partPath, finalPath, replace: false);
            File.SetAttributes(finalPath, File.GetAttributes(finalPath) & ~FileAttributes.Hidden);
            Results[index] = new DestFileResult(FileState.Written, null);
        }
        catch (Exception e)
        {
            CloseAndRemovePart();
            Results[index] = new DestFileResult(FileState.Failed, e.Message);
        }
    });

    public void Abort(int index, string reason) => worker.Post(() =>
    {
        CloseAndRemovePart();
        Results[index] = new DestFileResult(FileState.Failed, reason);
    });

    /// One FlushFileBuffers makes the drive empty its write cache — covering all the small files at once.
    public void FlushDevice() => worker.Invoke(() =>
    {
        var path = Path.Combine(CardPath, ".offload-flush");
        try
        {
            using var h = NativeIO.OpenWrite(path, unbuffered: false);
            NativeIO.Write(h, "x"u8, 0, path);
            NativeIO.Flush(h, path);
        }
        catch (IOError)
        {
            // Best effort: big files were flushed individually, verification reads everything back anyway.
        }
        finally
        {
            NativeIO.Delete(path);
        }
    });

    public void Wait() => worker.Wait();

    /// Re-reads every written file from the disk — once, through every hash in `kinds` — and compares it with the
    /// source hashes. The error texts stay English: they go into the report JSON shared with the Mac. `pause`: the
    /// read-back waits there before every file and every block (Pozastavit).
    public void Verify(IReadOnlyList<SourceFile> files, IReadOnlyList<FileHashes> sourceHashes, HashKind kinds, CancellationToken ct,
        PauseGate? pause = null)
    {
        using var buf = new AlignedBuffer(options.ChunkSize);
        bool md5 = HashKinds.Normalize(kinds).HasFlag(HashKind.Md5);
        for (int i = 0; i < files.Count; i++)
        {
            if (Results[i].State != FileState.Written) continue;
            if (ct.IsCancellationRequested || (pause != null && !pause.WaitWhilePaused(ct))) return;
            var f = files[i];
            try
            {
                var (got, size) = Hashing.HashFile(Layout.Join(CardPath, f.RelPath), buf, kinds, n => Interlocked.Add(ref verified, n), ct,
                    pause == null ? null : () => pause.WaitWhilePaused(ct));
                var want = sourceHashes[i];
                if (size != f.Size)
                    Results[i] = new DestFileResult(FileState.Failed, $"size mismatch: {size} != {f.Size}");
                else if (got.Xxh64 != want.Xxh64)
                    Results[i] = new DestFileResult(FileState.Failed, $"HASH MISMATCH: dest {got.Xxh64Hex} != source {want.Xxh64Hex ?? "-"}");
                else if (md5 && (want.Md5 == null || !got.Md5!.AsSpan().SequenceEqual(want.Md5)))
                    Results[i] = new DestFileResult(FileState.Failed, $"MD5 MISMATCH: dest {got.Md5Hex} != source {want.Md5Hex ?? "-"}");
                else
                    Results[i] = new DestFileResult(FileState.Verified, null);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                Results[i] = new DestFileResult(FileState.Failed, "verify: " + e.Message);
            }
        }
    }

    void CloseAndRemovePart()
    {
        handle?.Dispose();
        handle = null;
        if (partPath.Length > 0) NativeIO.Delete(partPath);
    }

    public void Dispose()
    {
        worker.Dispose();
        CloseAndRemovePart();
    }
}
