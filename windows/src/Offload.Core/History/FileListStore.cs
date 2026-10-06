using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Offload.Core.History;

/// The file list of one history entry (`history\<id>.files.gz`): gzip of UTF-8 lines "relpath\tsize\tmtimeTicksUtc\n".
/// Read only when a card's fingerprint differs from the entry's (spec 2.1, 5.5: ~200 KB for 10 000 files).
public static class FileListStore
{
    /// Written via a temporary name and a rename, like every other file Offload keeps.
    public static void Write(string path, IEnumerable<SourceFile> files)
    {
        using var buffer = new MemoryStream();
        using (var gz = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        using (var w = new StreamWriter(gz, new UTF8Encoding(false)))
        {
            foreach (var f in files)
            {
                w.Write(f.RelPath);
                w.Write('\t');
                w.Write(f.Size.ToString(CultureInfo.InvariantCulture));
                w.Write('\t');
                w.Write(f.ModifiedUtc.Ticks.ToString(CultureInfo.InvariantCulture));
                w.Write('\n');
            }
        }
        NativeIO.Mkdirs(Path.GetDirectoryName(path)!);
        NativeIO.WriteFileDurably(path, buffer.ToArray());
    }

    /// Null when the list is missing or damaged — that only costs the "new files" count, never the history line.
    public static List<SourceFile>? Read(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var gz = new GZipStream(file, CompressionMode.Decompress);
            using var r = new StreamReader(gz, Encoding.UTF8);
            var list = new List<SourceFile>();
            while (r.ReadLine() is string line)
            {
                if (line.Length == 0) continue;
                var parts = line.Split('\t');
                if (parts.Length != 3
                    || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var size)
                    || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                    || ticks > DateTime.MaxValue.Ticks) return null;
                list.Add(new SourceFile(parts[0], size, new DateTime(ticks, DateTimeKind.Utc)));
            }
            return list;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }
}
