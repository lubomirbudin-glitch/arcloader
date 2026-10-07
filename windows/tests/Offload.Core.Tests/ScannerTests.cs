namespace Offload.Core.Tests;

public class ScannerTests
{
    [Fact]
    public void Scan_ListsFilesSortedWithSizesAndTimes_SkipsJunk()
    {
        using var dir = new TempDir();
        var root = dir.Sub("card");
        TestFs.WriteRandom(Path.Combine(root, "CLIP", "b.mov"), 10, 1);
        TestFs.WriteRandom(Path.Combine(root, "CLIP", "a.mov"), 20, 2);
        TestFs.WriteRandom(Path.Combine(root, "root.xml"), 5, 3);
        TestFs.WriteRandom(Path.Combine(root, ".DS_Store"), 1, 4);
        TestFs.WriteRandom(Path.Combine(root, "CLIP", "._a.mov"), 1, 5);
        TestFs.WriteRandom(Path.Combine(root, "Thumbs.db"), 1, 6);
        TestFs.WriteRandom(Path.Combine(root, "System Volume Information", "x"), 1, 7);
        TestFs.WriteRandom(Path.Combine(root, ".Trashes", "y"), 1, 8);
        var hidden = Path.Combine(root, "CLIP", "hidden.bin");
        TestFs.WriteRandom(hidden, 3, 9);
        File.SetAttributes(hidden, FileAttributes.Hidden);
        dir.Sub("card", "EMPTY");
        var when = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(root, "CLIP", "a.mov"), when);

        var scan = Scanner.Scan(root);

        Assert.Equal(["CLIP/a.mov", "CLIP/b.mov", "CLIP/hidden.bin", "root.xml"], scan.Files.Select(f => f.RelPath));
        Assert.Equal(["CLIP", "EMPTY"], scan.Dirs);
        Assert.Equal(38, scan.TotalBytes);
        Assert.Equal(when, scan.Files[0].ModifiedUtc);
        Assert.Equal(
            new[] { ".DS_Store", ".Trashes", "CLIP/._a.mov", "System Volume Information", "Thumbs.db" }.Order(StringComparer.Ordinal),
            scan.Skipped.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Scan_MissingRoot_ThrowsIOError() =>
        Assert.Throws<IOError>(() => Scanner.Scan(@"C:\offload-no-such-folder"));

    [Fact]
    public void WindowsProblems_FindsCaseCollisionsAndInvalidNames()
    {
        var scan = new SourceScan
        {
            Root = @"X:\",
            Files =
            [
                new SourceFile("CLIP/A001.MOV", 1, DateTime.UtcNow),
                new SourceFile("CLIP/a001.mov", 1, DateTime.UtcNow),
                new SourceFile("CLIP/bad:name.mov", 1, DateTime.UtcNow),
                new SourceFile("CLIP/ok.mov", 1, DateTime.UtcNow),
            ],
            Dirs = ["CLIP"],
            Skipped = [],
        };
        var problems = Scanner.WindowsProblems(scan);
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.StartsWith("CLIP/a001.mov") && p.Contains("liší se jen velikostí písmen"));
        Assert.Contains(problems, p => p.StartsWith("CLIP/bad:name.mov"));
    }
}
