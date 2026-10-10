using System.Security.Cryptography;
using System.Text;

namespace Offload.Core.Append;

/// One append at a time per card folder (fix round 1, item 1): two appends into the same backup at once (the GUI and
/// the CLI) would number the same generation and rewrite the same hash list and chain. Held from before planning until
/// stage 1 ends, never waited for — a busy folder refuses the job at once. Named mutexes in the session namespace, like
/// the history lock; one per card folder, taken in a stable order so two jobs can never deadlock. A full copy goes into
/// a new folder and needs none — unless it verifies with its proxies (plan 10, ruling I4): a job whose reports say
/// verifyPending holds its folders from before it writes them until the verification results are written (AcquireHeld),
/// so no append, repair or resume touches a generation that is still being verified.
///
/// Gaps (fix round 2, item 10; also in docs/ROZDILY.md):
/// - `Local\` is one Windows session. An append from another session (another user logged in, a service) or from
///   another machine into the same share takes its own lock and is not stopped — there the no-replace writes of the
///   `_NNNN` reports and the MHL generation are the only guard, after its data files are copied. Such a job may also
///   delete a running repair's finished copy waiting under its hidden `.offload-part` name (re-review of the pause:
///   DestWriter.Begin deletes an orphan part before it writes one) — never silently: the repair's rename into place then
///   fails and its file ends Failed; "Copy again" again.
/// - The name comes from the spelling resolved to a volume and a path on it, not from the folder's identity: a `subst`
///   drive, `\\localhost\D$` or an 8.3 short name of the same folder gives another name, so two
///   appends spelled that way are not stopped either.
/// - Ověřit zálohu (BackupVerifier, GUI and `offload verify`) does not take this lock: it only reads. A verify running
///   while a repair (plan 9) sets a bad copy aside can see that file missing for the moment between the move to
///   `_REPORT\neshoda_NNNN` and the rename of the new copy into place — run it again after the repair; nothing is lost.
public sealed class AppendLock : IDisposable
{
    const string Prefix = @"Local\Offload-append-";
    readonly List<Mutex> held;

    AppendLock(List<Mutex> held) => this.held = held;

    /// `Local\Offload-append-<sha256 hex>` of "<volume id>|<card folder relative to the volume root>", upper-cased — the
    /// same for every spelling of the folder (case, trailing slash) and for the same disk under another letter.
    public static string Name(string cardPath) =>
        Prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(NativeIO.FolderKey(cardPath))));

    /// Takes the folders' locks, or throws OffloadError.Preflight(AppendBusy) naming the busy folder (nothing held then).
    /// Release on the thread that took them (Dispose).
    public static AppendLock Acquire(IReadOnlyList<string> cardPaths)
    {
        var held = new List<Mutex>();
        try
        {
            foreach (var (name, path) in cardPaths.Select(p => (Name(p), p)).DistinctBy(x => x.Item1).OrderBy(x => x.Item1, StringComparer.Ordinal))
            {
                Mutex? mutex = null;
                bool owned;
                try
                {
                    mutex = new Mutex(false, name);
                    owned = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    owned = true;   // its holder died; the folder's files were replaced atomically, the planner checks them
                }
                catch (UnauthorizedAccessException)
                {
                    owned = false;  // held by an elevated process: busy
                }
                if (!owned)
                {
                    mutex?.Dispose();
                    throw OffloadError.Preflight(PreflightCode.AppendBusy, Msg.AppendBusy(path));
                }
                held.Add(mutex!);
            }
            return new AppendLock(held);
        }
        catch
        {
            Release(held);
            throw;
        }
    }

    /// Ruling I4 (plan 10): the folders' locks held by a thread of their own, so they outlive the thread that took them —
    /// a job takes them in stage 1 (before its reports say verifyPending) and lets go on its stage-2 thread once the
    /// verification results are written (a mutex can only be released by the thread that owns it). Refuses as Acquire
    /// does, at once (nothing held then). Dispose from any thread, any number of times.
    public static IDisposable AcquireHeld(IReadOnlyList<string> cardPaths)
    {
        using var ready = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        Exception? refused = null;
        var holder = new Thread(() =>
        {
            AppendLock? held = null;
            try { held = Acquire(cardPaths); }
            catch (Exception e) { refused = e; }
            ready.Set();
            if (held == null) return;
            release.Wait();
            held.Dispose();
        }) { IsBackground = true, Name = "offload.folder-lock" };
        holder.Start();
        ready.Wait();
        if (refused != null)
        {
            holder.Join();
            release.Dispose();
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(refused);
        }
        return new HeldElsewhere(holder, release);
    }

    sealed class HeldElsewhere(Thread holder, ManualResetEventSlim release) : IDisposable
    {
        int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            release.Set();
            holder.Join();
            release.Dispose();
        }
    }

    public void Dispose() => Release(held);

    static void Release(List<Mutex> mutexes)
    {
        for (int i = mutexes.Count - 1; i >= 0; i--)
        {
            mutexes[i].ReleaseMutex();
            mutexes[i].Dispose();
        }
        mutexes.Clear();
    }
}
