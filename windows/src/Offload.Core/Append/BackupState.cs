namespace Offload.Core.Append;

/// The whole backup of a card as its card folders' reports record it now (R4) — what a repair's job and history are
/// judged on (ruling I2): `Failed` paths whose newest record failed on any folder, `Unverified` paths that are good
/// everywhere but whose newest record was not read back somewhere (CardRecord.ReadBack — the same definition as the
/// day report's CardRecord.Verified).
public sealed record BackupState(int Failed, int Unverified)
{
    public static BackupState Of(IReadOnlyList<string> cardPaths)
    {
        var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unverified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int d = 0; d < cardPaths.Count; d++)
        {
            var record = CardRecord.Load(cardPaths[d]);
            failed.UnionWith(record.FailedFiles);
            foreach (var (path, rec) in record.Files)
                if (!CardRecord.ReadBack(rec, cardPaths[d], d)) unverified.Add(path);
        }
        unverified.ExceptWith(failed);
        return new BackupState(failed.Count, unverified.Count);
    }
}
