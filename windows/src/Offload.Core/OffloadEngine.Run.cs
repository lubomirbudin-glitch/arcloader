using Offload.Core.VerifyInProxy;

namespace Offload.Core;

public sealed partial class OffloadEngine
{
    public OffloadReport Run(OffloadPlan plan)
    {
        // Ruling M16: a repair records its kept copies "verified" — only ever after reading them back.
        if (plan.Mode == PlanMode.Repair && !Options.Verify) throw OffloadError.Preflight(PreflightCode.RepairConflict, Msg.RepairNeedsVerify);
        var files = plan.Scan.Files;
        var started = DateTime.UtcNow;
        var pausedBefore = Pauses.PausedTotal;
        long holdsChecked = Pauses.Holds;
        var cardPaths = plan.CardPaths;
        foreach (var path in cardPaths)
        {
            NativeIO.Mkdirs(path);
            // With the card folder (fix round 1, item 2): a copy in progress or one that stopped is a card for the day
            // report ("bez reportu"), not a foreign folder. Empty it means "no report" to plan 6 — no verify target
            // (needs a report or a hash list), no append — and the MHL ignores it.
            NativeIO.Mkdirs(Path.Combine(path, Layout.ReportDirName));
            foreach (var d in plan.Scan.Dirs) NativeIO.Mkdirs(Layout.Join(path, d));
        }
        var ws = cardPaths.Select((p, i) => new DestWriter(i, p, files.Count, Options, plan.Generation)).ToArray();
        var plannedNow = ws.Select(w => files.Where((f, i) => plan.Writes(i, w.Index)).Sum(f => f.Size)).ToArray();
        lock (gate)
        {
            this.plan = plan;
            writers = ws;
            planned = plan.Write == null ? null : plannedNow;
        }
        // Created before the try and disposed last: writer queues may still hold chunks that point into the pool's native buffers.
        BufferPool? pool = null;
        var hashers = new List<SourceHasher>();
        var unrecorded = new System.Collections.Concurrent.ConcurrentQueue<(int File, DestWriter[] Targets)>();
        // Fix round 1 (M-a): records of files not flushed on their own — written once FlushDevice emptied the drive's cache.
        var afterFlush = new List<(DestWriter Writer, ManifestRecord Record)>();
        try
        {
            pool = new BufferPool(Options.BufferCount, Options.ChunkSize);
            // A repair's copies take their place only after the copy (PlaceRepairCopies): it records them there.
            if (plan.Expected == null) lock (gate) recorder = new SerialWorker("offload.manifest");
            // xxh64 (with XXH3-128 and the block hashes, plan 10), and MD5 on its own thread (spec 3.5).
            hashers = SourceHasher.For(plan.Hashes, files.Count, Options.HashFactory, Options.ChunkSize);
            var sourceErrors = new string?[files.Count];

            // Copy: the card is read exactly once; every block fans out to the hasher and all destinations.
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                // A repair writes a file only where it failed; a destination with a good copy only reads it back (Keep) —
                // also when the repair is cancelled before this file: its good copy must never look unwritten.
                var targets = ws.Where(w => plan.Writes(i, w.Index)).ToArray();
                foreach (var w in ws.Except(targets)) w.Keep(i);
                if (targets.Length == 0) continue;   // nothing to copy: the card is not read for it
                if (IsCancelled)
                {
                    sourceErrors[i] = "cancelled";
                    continue;
                }
                if (!cardChanged)
                {
                    Options.BeforeFileCopy?.Invoke(i);
                    if (!Pauses.WaitWhilePaused(cts.Token))
                    {
                        sourceErrors[i] = "cancelled";
                        continue;
                    }
                    // Ruling I1: a pause held the copy since the last file — the card is identified again before
                    // anything more is read from it; another card (or none) means nothing more of it is copied.
                    if (Pauses.Holds != holdsChecked)
                    {
                        holdsChecked = Pauses.Holds;
                        cardChanged = !StillSameCard();
                    }
                }
                if (cardChanged)
                {
                    sourceErrors[i] = CardChangedWhilePaused;
                    lock (gate) errorCount++;
                    continue;
                }
                lock (gate)
                {
                    fileIndex = i;
                    currentFile = f.RelPath;
                }
                var failure = CopyFile(plan.Source, f, targets, hashers, pool);
                if (failure != null)
                {
                    sourceErrors[i] = failure;
                    foreach (var h in hashers) h.Abort();
                    foreach (var w in targets) w.Abort(i, "source: " + failure);
                    lock (gate) errorCount++;
                }
                else
                {
                    foreach (var h in hashers) h.Finish(i);
                    // A repair's new copy waits under its temporary name until the card's hash is known (below).
                    foreach (var w in targets) w.Finish(i, f, hold: plan.Expected != null);
                    if (recorder != null && targets.Length > 0)
                    {
                        unrecorded.Enqueue((i, targets));
                        recorder.Post(() => Record(plan, unrecorded, hashers, afterFlush));
                    }
                }
            }
            foreach (var h in hashers) h.Wait();
            foreach (var w in ws) w.Wait();
            recorder?.Wait();
            var hashes = SourceHasher.Combine(hashers, files.Count);
            var blocks = (ulong[]?[])hashers[0].Blocks.Clone();
            // A hash that threw leaves the file without it: the file failed, also when verification is off.
            for (int i = 0; i < files.Count; i++)
            {
                if (sourceErrors[i] != null || SourceHasher.ErrorOf(hashers, i) is not string hashError) continue;
                sourceErrors[i] = hashError;
                lock (gate) errorCount++;
            }
            var recorded = plan.Expected == null ? hashes : PlaceRepairCopies(plan, ws, hashes, sourceErrors);
            if (plan.Expected != null)
            {
                foreach (var i in Enumerable.Range(0, files.Count).Where(i => ws.Any(w => plan.Writes(i, w.Index))))
                    unrecorded.Enqueue((i, ws.Where(w => plan.Writes(i, w.Index)).ToArray()));
                RecordNow(plan, unrecorded, recorded, blocks, afterFlush);
            }
            Blocks = blocks;
            WriteRates = ws.Select(w => w.WriteRate).ToList();
            WrittenUnbuffered = ws.Select(w => w.Unbuffered).ToList();

            SetPhase(CopyPhase.Flush);
            Options.BeforeDeviceFlush?.Invoke();
            OnEachDestination(ws, w => w.FlushDevice());
            AppendAfterFlush(afterFlush);
            var copyFinished = DateTime.UtcNow;
            var copyPaused = Pauses.PausedTotal - pausedBefore;

            Options.BeforeVerify?.Invoke();
            // Plan 10: the read-back is left to stage 2 (the proxies read the copies) — not when the copy was cancelled or
            // the card changed (then the generation is written now, as without verify-in-proxy), never for a repair.
            bool defer = Options.DeferVerify && Options.Verify && !IsCancelled && !cardChanged && plan.Mode != PlanMode.Repair;
            if (Options.Verify && !IsCancelled && !defer)
            {
                SetPhase(CopyPhase.Verify);
                // A repair compares every copy — kept or new — with what the backup recorded (ruling C1).
                OnEachDestination(ws, w => w.Verify(files, recorded, plan.Hashes, cts.Token, Pauses));
            }
            // A kept copy this repair did not get to read again (cancelled) is untouched: its earlier read-back stands.
            for (int i = 0; plan.ReadBackBefore is { } before && i < files.Count; i++)
                foreach (var w in ws)
                    if (!plan.Writes(i, w.Index) && before[i][w.Index] && w.Results[i].State == FileState.Written)
                        w.Results[i] = new DestFileResult(FileState.Verified, null);
            var finished = DateTime.UtcNow;
            var verifyPaused = Pauses.PausedTotal - pausedBefore - copyPaused;

            SetPhase(CopyPhase.Report);
            var report = OffloadReport.Build(plan, recorded, sourceErrors, ws.Select(w => w.Results).ToList(),
                ws.Select(w => w.Unbuffered).ToList(), Options.Verify && !IsCancelled && !defer, started, copyFinished, finished, copyPaused, verifyPaused);
            if (defer) report.VerifyPending = true;
            if (plan.Mode == PlanMode.Repair)
            {
                var moved = ws.SelectMany(w => w.MovedAside.Select(m => new SetAsideCopy(w.Index, files[m.File].RelPath, m.MovedTo))).ToList();
                report.SetAside = moved.Count > 0 ? moved : null;
            }
            var reportErrors = new List<string>();
            foreach (var path in cardPaths)
            {
                // Deferred (plan 10): the reports now, the ASC MHL and the hash lists once verified (Commit, stage 2).
                try { report.Write(path, includeHashRecords: !defer, createNew: defer ? plan.Generation > 1 : null); }
                catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException) { reportErrors.Add(e.Message); }
            }
            SetPhase(CopyPhase.Done);
            if (reportErrors.Count > 0) throw OffloadError.Failed(Msg.ReportNotWritten(string.Join("; ", reportErrors)));
            return report;
        }
        finally
        {
            SerialWorker? r;
            lock (gate)
            {
                r = recorder;
                recorder = null;
            }
            r?.Dispose();   // runs what it still has first — before the writers it waits for are gone
            foreach (var w in ws) w.Dispose();
            foreach (var h in hashers) h.Dispose();
            pool?.Dispose();
        }
    }

    /// The recorder's step (spec 4.5): every file finished so far goes into the manifest of each destination where it is
    /// in its place — after the hashers and the writers are done with it; many small files in one write.
    void Record(OffloadPlan plan, System.Collections.Concurrent.ConcurrentQueue<(int File, DestWriter[] Targets)> unrecorded, List<SourceHasher> hashers,
        List<(DestWriter Writer, ManifestRecord Record)> afterFlush)
    {
        if (unrecorded.IsEmpty) return;
        foreach (var h in hashers) h.Wait();
        var xx = hashers[0];
        var md5 = hashers.FirstOrDefault(h => h.Kind == HashKind.Md5);
        var batch = new List<(int File, DestWriter[] Targets)>();
        while (unrecorded.TryDequeue(out var item)) batch.Add(item);
        foreach (var w in batch.SelectMany(b => b.Targets).Distinct()) w.Wait();
        var hashes = batch.ToDictionary(b => b.File, b => new FileHashes(
            xx.Values[b.File] is string x ? Convert.ToUInt64(x, 16) : null,
            md5?.Values[b.File] is string m ? Convert.FromHexString(m) : null, xx.Xxh128[b.File]));
        Append(plan, batch, hashes, i => xx.Blocks[i], afterFlush);
    }

    /// A repair's files, recorded after they took their place, with the hashes the report records.
    void RecordNow(OffloadPlan plan, System.Collections.Concurrent.ConcurrentQueue<(int File, DestWriter[] Targets)> unrecorded,
        FileHashes[] recorded, ulong[]?[] blocks, List<(DestWriter Writer, ManifestRecord Record)> afterFlush)
    {
        var batch = new List<(int File, DestWriter[] Targets)>();
        while (unrecorded.TryDequeue(out var item)) batch.Add(item);
        Append(plan, batch, batch.ToDictionary(b => b.File, b => recorded[b.File]), i => blocks[i], afterFlush);
    }

    /// Fix round 1 (M-a): a record says the file is on the disk — only true for a file flushed on its own (FlushedAlone);
    /// the others wait in `afterFlush` for FlushDevice.
    void Append(OffloadPlan plan, List<(int File, DestWriter[] Targets)> batch, Dictionary<int, FileHashes> hashes, Func<int, ulong[]?> blocksOf,
        List<(DestWriter Writer, ManifestRecord Record)> afterFlush)
    {
        foreach (var w in batch.SelectMany(b => b.Targets).Distinct())
        {
            var records = new List<ManifestRecord>();
            foreach (var (i, targets) in batch)
            {
                if (!targets.Contains(w) || w.Results[i].State != FileState.Written) continue;
                if (hashes[i] is not { Xxh64: ulong x } h || blocksOf(i) is not { } blocks) continue;
                var f = plan.Scan.Files[i];
                var record = new ManifestRecord
                {
                    Generation = plan.Generation, Path = f.RelPath, Size = f.Size, Modified = f.ModifiedUtc, Xxh64 = Format.Hex64(x),
                    Md5 = h.Md5Hex, Xxh128 = h.Xxh128, BlockSize = Options.ChunkSize, Blocks = ManifestRecord.Hex(blocks),
                };
                if (w.FlushedAlone[i]) records.Add(record);
                else lock (afterFlush) afterFlush.Add((w, record));
            }
            if (records.Count == 0) continue;
            // Best effort: without its record a file is only copied again by a resume, never trusted blindly.
            try { ManifestFile.Append(w.CardPath, records); }
            catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException) { }
        }
    }

    /// After FlushDevice: the records that waited for it, on every destination whose flush went through; none where it
    /// failed — that data may never have reached the disk (a resume reads those files again).
    static void AppendAfterFlush(List<(DestWriter Writer, ManifestRecord Record)> afterFlush)
    {
        List<(DestWriter Writer, ManifestRecord Record)> all;
        lock (afterFlush) all = [.. afterFlush];
        foreach (var group in all.GroupBy(x => x.Writer).Where(g => g.Key.DeviceFlushed))
        {
            try { ManifestFile.Append(group.Key.CardPath, group.Select(x => x.Record).ToList()); }
            catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException) { }
        }
    }

    /// The file error of a repaired file whose card did not give the hash the backup recorded (English, report JSON; the
    /// HTML says it in Czech): "card differs from the backup: xxh64 <now> != recorded <then>".
    internal static string? CardDiffers(FileHashes expected, FileHashes now)
    {
        if (expected.Xxh64 is ulong want && now.Xxh64 is ulong got && got != want)
            return $"card differs from the backup: xxh64 {Format.Hex64(got)} != recorded {Format.Hex64(want)}";
        if (expected.Md5 is { } wantMd5 && now.Md5 is { } gotMd5 && !gotMd5.AsSpan().SequenceEqual(wantMd5))
            return $"card differs from the backup: md5 {now.Md5Hex} != recorded {expected.Md5Hex}";
        return null;
    }

    /// Repair, after the copy (every writer idle): ruling C1 — a new copy takes its place (the copy that did not match
    /// moved aside first) only when the card gave exactly what the backup recorded; otherwise it is thrown away and the
    /// file fails, nothing at its place moved. Returns the hashes the report records and the read-back compares with:
    /// always the recorded ones (the card's own only where the backup has none).
    FileHashes[] PlaceRepairCopies(OffloadPlan plan, DestWriter[] ws, FileHashes[] hashes, string?[] sourceErrors)
    {
        var files = plan.Scan.Files;
        var recorded = new FileHashes[files.Count];
        for (int i = 0; i < files.Count; i++)
        {
            var expected = plan.Expected![i];
            // MD5 of a backup whose MD5 failed: the card's, when its xxh64 is the recorded one.
            bool same = hashes[i].Xxh64 == expected.Xxh64;
            recorded[i] = expected.Xxh64 == null ? hashes[i]
                : new FileHashes(expected.Xxh64, expected.Md5 ?? (same ? hashes[i].Md5 : null), expected.Xxh128 ?? (same ? hashes[i].Xxh128 : null));
            if (sourceErrors[i] == null && CardDiffers(expected, hashes[i]) is string differs)
            {
                sourceErrors[i] = differs;
                lock (gate) errorCount++;
            }
            foreach (var w in ws.Where(w => plan.Writes(i, w.Index)))
            {
                if (sourceErrors[i] == null) w.Commit(i, files[i], plan.SetAside?[i][w.Index]);
                else w.Discard(i, "source: " + sourceErrors[i]);
            }
        }
        return recorded;
    }

    /// Ruling I1: the card in the drive is the copied one (no check without SameCard). A different, unreadable (null) or
    /// failing answer is asked again IdentifyRetries times 250 ms apart, like a card read — only then the card changed.
    bool StillSameCard()
    {
        if (SameCard is not { } same) return true;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (same()) return true;
            }
            catch (Exception) { /* unreadable now: asked again */ }
            if (attempt >= IdentifyRetries || IsCancelled) return false;
            Thread.Sleep(250);
        }
    }

    /// Reads one file block by block and hands every block to the hashers and all writers. Returns the failure, or null.
    string? CopyFile(string sourceRoot, SourceFile f, DestWriter[] ws, IReadOnlyList<SourceHasher> hashers, BufferPool pool)
    {
        var src = Layout.Join(sourceRoot, f.RelPath);
        foreach (var w in ws) w.Begin(f);
        foreach (var h in hashers) h.Begin();
        FileReader reader;
        try { reader = new FileReader(src); }
        catch (Exception e) { return e.Message; }

        long offset = 0;
        using (reader)
        {
            // A pause waits here, between blocks and before taking a buffer: the writers drain what they hold and idle.
            while (!IsCancelled && Pauses.WaitWhilePaused(cts.Token))
            {
                var buf = pool.Take();
                int n;
                try { n = ReadWithRetries(reader, buf, offset); }
                catch (Exception e)
                {
                    pool.Give(buf);
                    return e.Message;
                }
                if (n == 0)
                {
                    pool.Give(buf);
                    break;
                }
                if (n < Options.ChunkSize) buf.Span[n..].Clear();   // unbuffered writes round up to the sector size: zero the overhang
                // Every hasher and every writer releases the block once (MD5 is one consumer more, spec 3.5).
                var chunk = new Chunk(buf, n, offset, ws.Length + hashers.Count, pool);
                foreach (var h in hashers) h.Update(chunk);
                foreach (var w in ws) w.Write(chunk);
                offset += n;
                Interlocked.Add(ref readBytes, n);
                if (n < Options.ChunkSize) break;
            }
        }
        if (IsCancelled) return "cancelled";
        if (offset != f.Size) return $"source size changed during copy ({offset} != {f.Size})";
        return null;
    }

    /// Card readers have bad moments; a failed read gets a few more tries before the file is given up.
    int ReadWithRetries(FileReader reader, AlignedBuffer buf, long offset)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return reader.Read(buf.Span, offset);
            }
            catch (IOError e)
            {
                if (attempt >= Options.ReadRetries) throw new IOException($"{e.Message} (after {Options.ReadRetries} retries)", e);
                Thread.Sleep(250);
            }
        }
    }

    /// One thread per destination, so every disk works at its own speed.
    static void OnEachDestination(DestWriter[] ws, Action<DestWriter> work)
    {
        var errors = new List<Exception>();
        var threads = ws.Select(w => new Thread(() =>
        {
            try { work(w); }
            catch (Exception e) { lock (errors) errors.Add(e); }
        }) { IsBackground = true }).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        if (errors.Count > 0) throw OffloadError.Failed(string.Join("; ", errors.Select(e => e.Message)));
    }
}
