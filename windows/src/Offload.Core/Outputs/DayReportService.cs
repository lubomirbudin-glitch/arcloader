using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Offload.Core.Outputs;

/// Spec 3.2 "Kdy se tvoří": the day report is made again after every job and on request — one writer at a time per day
/// folder: a SemaphoreSlim in this process, a named mutex `Local\Offload-day-<sha1>` between processes (the GUI and the
/// CLI). The files are derived (R25): the last writer wins, also against the Mac writing the same day (it takes no
/// Windows lock; ReplaceFile swaps every file whole, so a reader or the other writer never sees half of one).
public static class DayReportService
{
    /// How long a regeneration waits for another one of the same day (a PDF takes seconds per destination).
    public static readonly TimeSpan DefaultWait = TimeSpan.FromMinutes(2);

    static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);

    /// Tests only: called while the day's locks are held, before anything is written (this thread only).
    [ThreadStatic] internal static Action<string>? WhileHeld;

    /// `Local\Offload-day-<sha1 hex>` of the day's volume and its path on it (ruling M7, like AppendLock) — the same
    /// for every spelling of the folder: case, a trailing slash, another drive letter, the volume GUID path.
    public static string MutexName(string dayPath) =>
        @"Local\Offload-day-" + Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(NativeIO.FolderKey(dayPath))));

    /// The day folder a pick means: a card folder (it has `_REPORT\offload_report.json`) stands for its day.
    public static string DayFolderOf(string picked)
    {
        var full = Path.GetFullPath(picked).TrimEnd('\\');
        return File.Exists(Path.Combine(full, Layout.ReportDirName, CardRecord.MainReport)) && Path.GetDirectoryName(full) is string day ? day : full;
    }

    /// `<root>\<date>` after a job on that destination.
    public static string Regenerate(string root, string date, OutputSettings outputs, Action<string>? log = null, TimeSpan? wait = null) =>
        RegenerateDay(Path.Combine(root, date), outputs, log, wait);

    /// Writes the day report of `dayPath` and returns its HTML path. Throws (Czech message) when the folder is not there
    /// or is no day (DayReport.CheckDay: never a `_REPORT` in a destination root or a card), when another writer holds
    /// the day longer than `wait`, or when the HTML cannot be written; the PDF and the day's clip lists are best effort
    /// (logged).
    public static string RegenerateDay(string dayPath, OutputSettings outputs, Action<string>? log = null, TimeSpan? wait = null)
    {
        DayReport.CheckDay(dayPath);   // before waiting for anyone: a wrong folder is refused at once
        var limit = wait ?? DefaultWait;
        var clock = Stopwatch.StartNew();
        var gate = Gates.GetOrAdd(NativeIO.FolderKey(dayPath), _ => new SemaphoreSlim(1, 1));
        if (!gate.Wait(limit)) throw new TimeoutException(Msg.DayReportBusy(dayPath));
        try
        {
            Mutex? mutex = null;
            bool owned;
            try
            {
                mutex = new Mutex(false, MutexName(dayPath));
                var left = limit - clock.Elapsed;
                owned = mutex.WaitOne(left > TimeSpan.Zero ? left : TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                owned = true;   // its holder died; the files are swapped whole, so there is nothing half-written to fear
            }
            catch (UnauthorizedAccessException)
            {
                owned = false;  // held by an elevated process: busy
            }
            if (!owned)
            {
                mutex?.Dispose();
                throw new TimeoutException(Msg.DayReportBusy(dayPath));
            }
            try
            {
                WhileHeld?.Invoke(dayPath);
                return DayReport.Write(dayPath, outputs, log);
            }
            finally
            {
                mutex!.ReleaseMutex();
                mutex.Dispose();
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
