namespace Offload.Core.Append;

/// The whole backup of a card as its card folders' reports record it now (R4) — what a repair's job is judged on
/// (ruling I2): `Failed` paths whose newest record failed on any folder, `Unverified` paths that are good everywhere but
/// were never read back somewhere (a copy made without verification, a cancelled one).
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
            {
                int column = CardRecord.DestinationIndex(rec.Report, cardPaths[d], d);
                if (column >= rec.File.Destinations.Count || rec.File.Destinations[column] != "verified") unverified.Add(path);
            }
        }
        unverified.ExceptWith(failed);
        return new BackupState(failed.Count, unverified.Count);
    }
}
