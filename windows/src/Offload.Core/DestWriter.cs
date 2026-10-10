using System.Diagnostics;
using System.IO.Hashing;
using Microsoft.Win32.SafeHandles;
using Offload.Core.VerifyInProxy;

namespace Offload.Core;

/// Writes the stream to one destination. Each destination has its own thread, so disks run in parallel.
internal sealed class DestWriter : IDisposable
{
    readonly SerialWorker worker;
    readonly OffloadOptions options;
    readonly int index;
    readonly int generation;
    SafeFileHandle? handle;
    string partPath = "";
    string? currentError;
    int align = AlignedBuffer.Alignment;
    long written;
    long verified;
    volatile bool reading;
    /// The file being written, the XXH3-64 of its blocks written so far, and how many of them a checkpoint holds (R6).
    SourceFile? current;
    readonly List<ulong> blockHashes = [];
    int checkpointed;
    long writeTicks, writeBytes;

    /// Repair: finished copies held under their `.offload-part` name until the card's hash is known (Commit / Discard).
    readonly Dictionary<int, string> held = [];

    public string CardPath { get; }
    public int Index => index;
    public DestFileResult[] Results { get; }
    /// Repair: the copies that did not match, moved aside (file index, where to — relative); read after Wait.
    public List<(int File, string MovedTo)> MovedAside { get; } = [];
    /// False once this volume refused unbuffered writes and the writer fell back to the file cache.
    public bool Unbuffered { get; private set; }
    public long Written => Interlocked.Read(ref written);
    public long Verified => Interlocked.Read(ref verified);
    /// Fix round 1 (M-a): per file, its data was flushed to the disk on its own in Finish (a big file, or any file on a
    /// destination written through the file cache); the others reach the disk only with FlushDevice. Read after Wait.
    public bool[] FlushedAlone { get; }
    /// FlushDevice emptied the drive's write cache (false before it ran, or when it failed).
    public bool DeviceFlushed { get; private set; }
    /// Bytes per second while writing (the time inside the writes only); null before the first write. Plan 10: the
    /// fastest destination becomes the primary one (spec 6).
    public double? WriteRate
    {
        get
        {
            long ticks = Interlocked.Read(ref writeTicks);
            return ticks > 0 ? Interlocked.Read(ref writeBytes) / ((double)ticks / Stopwatch.Frequency) : null;
        }
    }

    /// Nothing is being written to or read back from this destination now: every block handed to it is on the disk
    /// (or failed there) and the read-back, if any, waits at the pause gate or is done. Pozastavit shows "Pozastaveno"
    /// only when every destination is idle — until then a disk must not be unplugged.
    public bool IsIdle => worker.IsIdle && !reading;

    /// `generation`: the ASC MHL generation this copy writes — the manifest's checkpoints say it (R6).
    public DestWriter(int index, string cardPath, int fileCount, OffloadOptions options, int generation = 1)
    {
        CardPath = cardPath;
        this.index = index;
        this.options = options;
        this.generation = generation;
        Unbuffered = !options.ForceBuffered;
        Results = new DestFileResult[fileCount];
        FlushedAlone = new bool[fileCount];
        worker = new SerialWorker($"offload.dest{index}");
    }

    /// In-progress name: ".<name>.offload-part" next to the final file, so an unfinished copy never looks finished.
    public static string PartName(string rel)
    {
        int slash = rel.LastIndexOf('/');
        var name = "." + rel[(slash + 1)..] + ".offload-part";
        return slash < 0 ? name : rel[..slash] + "/" + name;
    }

    /// `resume` (R6): the first blocks of the file already in its `.offload-part` from an earlier run (checked against the
    /// manifest's checkpoint by the planner) — the part is opened as it is and written on from there.
    public void Begin(SourceFile file, PartCheckpoint? resume = null) => worker.Post(() =>
    {
        currentError = null;
        current = file;
        blockHashes.Clear();
        checkpointed = 0;
        partPath = Layout.Join(CardPath, PartName(file.RelPath));
        try
        {
            if (resume != null)
            {
                // Ruling M1: a part is resumed only past the file cache. The engine asked before this writer may have
                // switched to buffered writes (finishing the previous file) — then the file fails at once: its part is
                // never truncated nor written through the cache (the next resume finds it as it was).
                if (!Unbuffered)
                {
                    partPath = "";
                    currentError = "resume: the destination writes through the file cache now";
                    return;
                }
                var h = NativeIO.OpenResume(partPath);
                if (!NativeIO.WritesPastTheCache(h) || options.ChunkSize % NativeIO.SectorSize(h) != 0)
                {
                    h.Dispose();
                    partPath = "";
                    currentError = "resume: the part cannot be written past the file cache";
                    return;
                }
                handle = h;
                align = NativeIO.SectorSize(h);
                blockHashes.AddRange(resume.Blocks);
                checkpointed = resume.Blocks.Length;
            }
            else handle = OpenPart(file.Size);
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
            // Fix round 1 (M-b): the flags asked for are not proof — a handle that lost NO_BUFFERING | WRITE_THROUGH writes
            // through the file cache: a buffered destination (no checkpoints, every file flushed, not a primary).
            if (!NativeIO.WritesPastTheCache(h))
            {
                Unbuffered = false;
                return h;
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
            long started = Stopwatch.GetTimestamp();
            try
            {
                NativeIO.Write(handle, c.Buffer.Span[..len], c.Offset, partPath);
            }
            catch (IOError e) when (Unbuffered && e.Code == NativeIO.ErrorInvalidParameter)
            {
                SwitchToBuffered();
                NativeIO.Write(handle!, c.Buffer.Span[..c.Length], c.Offset, partPath);
            }
            Interlocked.Add(ref writeTicks, Stopwatch.GetTimestamp() - started);
            Interlocked.Add(ref writeBytes, c.Length);
            Interlocked.Add(ref written, c.Length);
            blockHashes.Add(XxHash3.HashToUInt64(c.Buffer.Span[..c.Length]));
            if (options.CheckpointBlocks > 0 && blockHashes.Count - checkpointed >= options.CheckpointBlocks) Checkpoint();
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

    /// R6: the blocks written since the last checkpoint go into the manifest — on the disk already (written through), so a
    /// later append resumes after them. Only past the file cache: a buffered write may still be in RAM. Best effort: a
    /// checkpoint that cannot be written only means a resume starts earlier.
    void Checkpoint()
    {
        if (!Unbuffered || current is not { } f) return;
        try
        {
            ManifestFile.Append(CardPath, new ManifestRecord
            {
                Kind = ManifestRecord.PartKind, Generation = generation, Path = f.RelPath, Size = f.Size, Modified = f.ModifiedUtc,
                BlockSize = options.ChunkSize, First = checkpointed, Blocks = ManifestRecord.Hex(blockHashes.Skip(checkpointed)),
            });
            checkpointed = blockHashes.Count;
        }
        catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException) { }
    }

    void SwitchToBuffered()
    {
        handle?.Dispose();
        Unbuffered = false;
        handle = NativeIO.OpenExisting(partPath);
    }

    /// `hold` (repair): the finished copy stays under its `.offload-part` name — nothing at the file's place changes —
    /// until Commit or Discard; its result stays Pending until then.
    public void Finish(int index, SourceFile file, bool hold = false) => worker.Post(() =>
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
            bool alone = file.Size >= options.FullSyncThreshold || !Unbuffered;
            if (alone) NativeIO.Flush(handle, partPath);
            FlushedAlone[index] = alone;
            NativeIO.SetModified(handle, file.ModifiedUtc, partPath);
            handle.Dispose();
            handle = null;
            if (hold)
            {
                held[index] = partPath;
                partPath = "";   // not this writer's current part any more: Begin and Dispose leave it alone
                return;
            }
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

    /// Repair: this destination already holds a good copy of file `index` — nothing is written, the read-back checks it.
    public void Keep(int index) => worker.Post(() => Results[index] = new DestFileResult(FileState.Written, null));

    /// Repair, after Wait (the worker is idle): the card gave what the backup recorded — the held copy of file `index`
    /// takes its place. A copy there that did not match is first moved to `setAside` (relative to the card folder; same
    /// volume, never replacing anything, never deleted). A move that fails fails the file here and leaves the copy at
    /// its place as it was. A file whose write failed (nothing held) keeps its result.
    public void Commit(int index, SourceFile file, string? setAside)
    {
        if (!held.Remove(index, out var part)) return;
        var finalPath = Layout.Join(CardPath, file.RelPath);
        var aside = setAside == null ? null : Layout.Join(CardPath, setAside);
        try
        {
            if (aside != null)
            {
                NativeIO.Mkdirs(Path.GetDirectoryName(aside)!);
                NativeIO.Move(finalPath, aside, replace: false);
            }
        }
        catch (Exception e)
        {
            NativeIO.Delete(part);
            Results[index] = new DestFileResult(FileState.Failed, "set aside: " + e.Message);
            return;
        }
        bool placed = false;
        try
        {
            NativeIO.Move(part, finalPath, replace: false);
            placed = true;
            if (setAside != null) MovedAside.Add((index, setAside));
            File.SetAttributes(finalPath, File.GetAttributes(finalPath) & ~FileAttributes.Hidden);
            Results[index] = new DestFileResult(FileState.Written, null);
        }
        catch (Exception e)
        {
            if (!placed)
            {
                NativeIO.Delete(part);
                // The bad copy goes back to its place (never over anything); if even that fails it stays set aside.
                if (aside != null)
                {
                    try { NativeIO.Move(aside, finalPath, replace: false); }
                    catch (Exception) { MovedAside.Add((index, setAside!)); }
                }
            }
            Results[index] = new DestFileResult(FileState.Failed, e.Message);
        }
    }

    /// Repair, after Wait: the card did not give what the backup recorded — the held copy of file `index` is thrown away;
    /// nothing at the file's place is moved or written.
    public void Discard(int index, string reason)
    {
        if (!held.Remove(index, out var part)) return;
        NativeIO.Delete(part);
        Results[index] = new DestFileResult(FileState.Failed, reason);
    }

    /// One FlushFileBuffers makes the drive empty its write cache — covering all the small files at once.
    public void FlushDevice() => worker.Invoke(() =>
    {
        var path = Path.Combine(CardPath, ".offload-flush");
        try
        {
            using var h = NativeIO.OpenWrite(path, unbuffered: false);
            NativeIO.Write(h, "x"u8, 0, path);
            NativeIO.Flush(h, path);
            DeviceFlushed = true;
        }
        catch (IOError)
        {
            DeviceFlushed = false;
            // Best effort: big files were flushed individually, verification reads everything back anyway.
        }
        finally
        {
            NativeIO.Delete(path);
        }
    });

    public void Wait() => worker.Wait();

    volatile string? verifying;
    /// The file the read-back reads now (relative), null when none — what "Pozastavuji (dokončuji …)" names.
    public string? VerifyingFile => verifying;

    long verifyPlanned = -1;
    /// What the read-back will read on this destination (files written in this run); -1 until it starts.
    public long VerifyPlanned => Interlocked.Read(ref verifyPlanned);

    /// Re-reads every written file from the disk — once, through every hash in `kinds` — and compares it with the
    /// source hashes. The error texts stay English: they go into the report JSON shared with the Mac. `pause`: the
    /// read-back's membership of the pause gate — it waits there before every file, never inside one (Pozastavit lets
    /// the file being read finish, task 2026-10-09).
    public void Verify(IReadOnlyList<SourceFile> files, IReadOnlyList<FileHashes> sourceHashes, HashKind kinds, CancellationToken ct,
        PauseGate.Worker? pause = null)
    {
        using var buf = new AlignedBuffer(options.ChunkSize);
        bool md5 = HashKinds.Normalize(kinds).HasFlag(HashKind.Md5);
        // Not reading while waiting at the gate (IsIdle).
        bool Hold()
        {
            reading = false;
            try { return pause!.WaitWhilePaused(ct); }
            finally { reading = true; }
        }
        Interlocked.Exchange(ref verifyPlanned, Enumerable.Range(0, files.Count).Where(i => Results[i].State == FileState.Written).Sum(i => files[i].Size));
        reading = true;
        try
        {
            VerifyFiles(files, sourceHashes, kinds, ct, pause == null ? null : Hold, buf, md5);
        }
        finally
        {
            reading = false;
        }
    }

    void VerifyFiles(IReadOnlyList<SourceFile> files, IReadOnlyList<FileHashes> sourceHashes, HashKind kinds, CancellationToken ct,
        Func<bool>? hold, AlignedBuffer buf, bool md5)
    {
        for (int i = 0; i < files.Count; i++)
        {
            if (Results[i].State != FileState.Written) continue;
            if (ct.IsCancellationRequested || (hold != null && !hold())) return;
            var f = files[i];
            try
            {
                verifying = f.RelPath;
                var (got, size) = Hashing.HashFile(Layout.Join(CardPath, f.RelPath), buf, kinds, n => Interlocked.Add(ref verified, n), ct,
                    options.BeforeReadBackBlock is { } hook ? () => hook(index) : null);
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
            finally
            {
                verifying = null;
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
        foreach (var part in held.Values) NativeIO.Delete(part);   // a run that stopped before Commit / Discard
        held.Clear();
    }
}
