namespace Offload.Core.Tests;

/// Unique folder under %TEMP%\offload-tests, deleted on Dispose.
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "offload-tests", Guid.NewGuid().ToString("N")[..8]);

    public TempDir() => Directory.CreateDirectory(Path);

    public string Sub(params string[] parts)
    {
        var p = System.IO.Path.Combine([Path, .. parts]);
        Directory.CreateDirectory(p);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public static class TestFs
{
    public static byte[] WriteRandom(string path, int size, int seed)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        File.WriteAllBytes(path, data);
        return data;
    }
}

public static class Reports
{
    /// Two-file report for one destination at `cardPath`; `destError` makes the second file fail there.
    public static OffloadReport Sample(string cardPath, bool verified = true, string? destError = null, bool unbuffered = true)
    {
        var t0 = new DateTime(2026, 10, 4, 8, 0, 0, DateTimeKind.Utc);
        var ok = verified ? "verified" : "written";
        return new OffloadReport
        {
            CardName = "A001",
            Date = "2026-10-04",
            Source = @"E:\",
            SourceFilesystem = "exFAT",
            Started = t0,
            CopyFinished = t0.AddSeconds(10),
            Finished = t0.AddSeconds(15),
            Verified = verified,
            TotalFiles = 2,
            TotalBytes = 3000,
            Destinations =
            [
                new ReportDestination
                {
                    Root = Path.GetDirectoryName(Path.GetDirectoryName(cardPath))!, CardPath = cardPath, Filesystem = "NTFS",
                    FilesOK = destError == null ? 2 : 1, FilesFailed = destError == null ? 0 : 1,
                    Unbuffered = unbuffered ? null : false,
                },
            ],
            Files =
            [
                new ReportFile { Path = "CLIP/A&B.mov", Size = 1000, Modified = t0, Xxh64 = "0123456789abcdef", Destinations = [ok], Errors = [null] },
                new ReportFile { Path = "CLIP/c 02.mov", Size = 2000, Modified = t0, Xxh64 = "fedcba9876543210",
                                 Destinations = [destError == null ? ok : "failed"], Errors = [destError] },
            ],
            Skipped = [".DS_Store"],
        };
    }
}
