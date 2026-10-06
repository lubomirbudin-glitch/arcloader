using System.Security.Cryptography;
using System.Text;

namespace Offload.Core.Append;

/// One append at a time per card folder (fix round 1, item 1): two appends into the same backup at once (the GUI and
/// the CLI) would number the same generation and rewrite the same hash list and chain. Held from before planning until
/// stage 1 ends, never waited for — a busy folder refuses the job at once. Named mutexes in the session namespace, like
/// the history lock; one per card folder, taken in a stable order so two jobs can never deadlock. A full copy goes into
/// a new folder and needs none.
///
/// Gaps (fix round 2, item 10; also in docs/ROZDILY.md):
/// - `Local\` is one Windows session. An append from another session (another user logged in, a service) or from
///   another machine into the same share takes its own lock and is not stopped — there the no-replace writes of the
///   `_NNNN` reports and the MHL generation are the only guard, after its data files are copied.
/// - The name comes from the spelling resolved to a volume and a path on it, not from the folder's identity: a `subst`
///   drive, `\\localhost\D$` or an 8.3 short name of the same folder gives another name, so two
///   appends spelled that way are not stopped either.
public sealed class AppendLock : IDisposable
{
    const string Prefix = @"Local\Offload-append-";
    readonly List<Mutex> held;

    AppendLock(List<Mutex> held) => this.held = held;

    /// `Local\Offload-append-<sha256 hex>` of "<volume id>|<card folder relative to the volume root>", upper-cased — the
    /// same for every spelling of the folder (case, trailing slash) and for the same disk under another letter.
    public static string Name(string cardPath)
    {
        var full = Path.GetFullPath(cardPath).TrimEnd('\\');
        var root = NativeIO.VolumeRoot(full);
        var key = root != null && NativeIO.VolumeId(root) is string volume && full.StartsWith(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            ? volume + "|" + Path.GetRelativePath(root, full)
            : "|" + full;
        return Prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToUpperInvariant())));
    }

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
