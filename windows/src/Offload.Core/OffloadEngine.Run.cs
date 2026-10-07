namespace Offload.Core;

public sealed partial class OffloadEngine
{
    public OffloadReport Run(OffloadPlan plan)
    {
        var files = plan.Scan.Files;
        var started = DateTime.UtcNow;
        var cardPaths = plan.CardPaths;
        foreach (var path in cardPaths)
        {
            NativeIO.Mkdirs(path);
            foreach (var d in plan.Scan.Dirs) NativeIO.Mkdirs(Layout.Join(path, d));
        }
        var ws = cardPaths.Select((p, i) => new DestWriter(i, p, files.Count, Options)).ToArray();
        lock (gate)
        {
            this.plan = plan;
            writers = ws;
        }
        // Created before the try and disposed last: writer queues may still hold chunks that point into the pool's native buffers.
        BufferPool? pool = null;
        var hashers = new List<SourceHasher>();
        try
        {
            pool = new BufferPool(Options.BufferCount, Options.ChunkSize);
            hashers = SourceHasher.For(plan.Hashes, files.Count);   // xxh64, and MD5 on its own thread (spec 3.5)
            var sourceErrors = new string?[files.Count];

            // Copy: the card is read exactly once; every block fans out to the hasher and all destinations.
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                if (IsCancelled)
                {
                    sourceErrors[i] = "cancelled";
                    continue;
                }
                Options.BeforeFileCopy?.Invoke(i);
                lock (gate)
                {
                    fileIndex = i;
                    currentFile = f.RelPath;
                }
                var failure = CopyFile(plan.Source, f, ws, hashers, pool);
                if (failure != null)
                {
                    sourceErrors[i] = failure;
                    foreach (var h in hashers) h.Abort();
                    foreach (var w in ws) w.Abort(i, "source: " + failure);
                    lock (gate) errorCount++;
                }
                else
                {
                    foreach (var h in hashers) h.Finish(i);
                    foreach (var w in ws) w.Finish(i, f);
                }
            }
            foreach (var h in hashers) h.Wait();
            foreach (var w in ws) w.Wait();
            var hashes = SourceHasher.Combine(hashers, files.Count);

            SetPhase(CopyPhase.Flush);
            OnEachDestination(ws, w => w.FlushDevice());
            var copyFinished = DateTime.UtcNow;

            Options.BeforeVerify?.Invoke();
            if (Options.Verify && !IsCancelled)
            {
                SetPhase(CopyPhase.Verify);
                OnEachDestination(ws, w => w.Verify(files, hashes, plan.Hashes, cts.Token));
            }
            var finished = DateTime.UtcNow;

            SetPhase(CopyPhase.Report);
            var report = OffloadReport.Build(plan, hashes, sourceErrors, ws.Select(w => w.Results).ToList(),
                ws.Select(w => w.Unbuffered).ToList(), Options.Verify && !IsCancelled, started, copyFinished, finished);
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
            while (!IsCancelled)
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
