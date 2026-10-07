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
        var ws = cardPaths.Select((p, i) => new DestWriter(i, p, files.Count, Options)).ToArray();
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
        try
        {
            pool = new BufferPool(Options.BufferCount, Options.ChunkSize);
            hashers = SourceHasher.For(plan.Hashes, files.Count, Options.HashFactory);   // xxh64, and MD5 on its own thread (spec 3.5)
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
                }
            }
            foreach (var h in hashers) h.Wait();
            foreach (var w in ws) w.Wait();
            var hashes = SourceHasher.Combine(hashers, files.Count);
            // A hash that threw leaves the file without it: the file failed, also when verification is off.
            for (int i = 0; i < files.Count; i++)
            {
                if (sourceErrors[i] != null || SourceHasher.ErrorOf(hashers, i) is not string hashError) continue;
                sourceErrors[i] = hashError;
                lock (gate) errorCount++;
            }
            var recorded = plan.Expected == null ? hashes : PlaceRepairCopies(plan, ws, hashes, sourceErrors);

            SetPhase(CopyPhase.Flush);
            OnEachDestination(ws, w => w.FlushDevice());
            var copyFinished = DateTime.UtcNow;
            var copyPaused = Pauses.PausedTotal - pausedBefore;

            Options.BeforeVerify?.Invoke();
            if (Options.Verify && !IsCancelled)
            {
                SetPhase(CopyPhase.Verify);
                // A repair compares every copy — kept or new — with what the backup recorded (ruling C1).
                OnEachDestination(ws, w => w.Verify(files, recorded, plan.Hashes, cts.Token, Pauses));
            }
            var finished = DateTime.UtcNow;
            var verifyPaused = Pauses.PausedTotal - pausedBefore - copyPaused;

            SetPhase(CopyPhase.Report);
            var report = OffloadReport.Build(plan, recorded, sourceErrors, ws.Select(w => w.Results).ToList(),
                ws.Select(w => w.Unbuffered).ToList(), Options.Verify && !IsCancelled, started, copyFinished, finished, copyPaused, verifyPaused);
            if (plan.Mode == PlanMode.Repair)
            {
                var moved = ws.SelectMany(w => w.MovedAside.Select(m => new SetAsideCopy(w.Index, files[m.File].RelPath, m.MovedTo))).ToList();
                report.SetAside = moved.Count > 0 ? moved : null;
            }
            var reportErrors = new List<string>();
            foreach (var path in cardPaths)
            {
                try { report.Write(path, includeHashRecords: true); }
                catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException) { reportErrors.Add(e.Message); }
            }
            SetPhase(CopyPhase.Done);
            if (reportErrors.Count > 0) throw OffloadError.Failed(Msg.ReportNotWritten(string.Join("; ", reportErrors)));
            return report;
        }
        finally
        {
            foreach (var w in ws) w.Dispose();
            foreach (var h in hashers) h.Dispose();
            pool?.Dispose();
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
            recorded[i] = expected.Xxh64 == null ? hashes[i]
                : new FileHashes(expected.Xxh64, expected.Md5 ?? (hashes[i].Xxh64 == expected.Xxh64 ? hashes[i].Md5 : null));
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
