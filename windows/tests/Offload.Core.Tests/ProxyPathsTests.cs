namespace Offload.Core.Tests;

/// 0.5.1: `_PROXY` mirrors the card's folders (`_PROXY\idle\1.mov`), so clips of the same name in different folders keep
/// their names (relinking by name); an earlier flat backup stays flat.
public class ProxyPathsTests
{
    static Clip V(string path, ClipFormat format = ClipFormat.Video) =>
        new() { Name = Path.GetFileNameWithoutExtension(path.TrimEnd('/')), Path = path, Format = format, Segments = [path], Bytes = 1 };

    static ClipEntry Done(string clipPath, string? proxyPath) =>
        new() { Clip = V(clipPath), ProxyStatus = proxyPath == null ? ProxyStatus.Skipped : ProxyStatus.Done, ProxyPath = proxyPath };

    [Theory]
    [InlineData("idle/1.mp4", "idle")]
    [InlineData("1.mp4", "")]
    [InlineData("PRIVATE/M4ROOT/CLIP/C0001.MP4", "PRIVATE/M4ROOT/CLIP")]
    [InlineData("CONTENTS/CLIPS001/AA0012/AA0012.MXF", "CONTENTS/CLIPS001/AA0012")]
    public void Folder_IsTheClipsFolderOnTheCard(string path, string folder) => Assert.Equal(folder, ProxyPaths.Folder(V(path)));

    [Fact]
    public void Folder_ArriSequenceIsItsParent_R3dItsSegmentsFolder()
    {
        // An .ari sequence's Path is the sequence folder itself (ClipDetector): the proxy sits next to it, named after it.
        var ari = new Clip { Name = "A001C001", Path = "ARRI/A001C001", Format = ClipFormat.Arriraw, Segments = ["ARRI/A001C001/f.0001.ari"], Bytes = 1 };
        Assert.Equal("ARRI", ProxyPaths.Folder(ari));
        Assert.Equal("_PROXY/ARRI/A001C001.mov", ProxyPaths.Relative(ProxyPaths.Folder(ari), ari.Name));
        var r3d = new Clip { Name = "A001_C002_0101AB", Path = "A001_C002.RDC/A001_C002_0101AB_001.R3D", Format = ClipFormat.R3d, Segments = ["A001_C002.RDC/A001_C002_0101AB_001.R3D"], Bytes = 1 };
        Assert.Equal("_PROXY/A001_C002.RDC/A001_C002_0101AB.mov", ProxyPaths.Relative(ProxyPaths.Folder(r3d), r3d.Name));
    }

    [Fact]
    public void Relative_MirrorsTheFolder_RootStaysAtTop()
    {
        Assert.Equal("_PROXY/idle/1.mov", ProxyPaths.Relative("idle", "1"));
        Assert.Equal("_PROXY/1.mov", ProxyPaths.Relative("", "1"));
    }

    [Fact]
    public void Name_SameNameInOtherFolders_NotRenamed_SameFolder_GetsSuffix()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(("_PROXY/idle/1.mov", "1"), ProxyPaths.Pick(V("idle/1.mp4"), ProxyTree.Mirror, used));
        Assert.Equal(("_PROXY/listening/1.mov", "1"), ProxyPaths.Pick(V("listening/1.mp4"), ProxyTree.Mirror, used));
        // A.mov and A.mxf in one folder: the second one is A_2 (the note says so).
        Assert.Equal(("_PROXY/idle/A.mov", "A"), ProxyPaths.Pick(V("idle/A.mov"), ProxyTree.Mirror, used));
        Assert.Equal(("_PROXY/IDLE/A_2.mov", "A_2"), ProxyPaths.Pick(V("IDLE/A.mxf"), ProxyTree.Mirror, used));   // one folder on NTFS/exFAT
    }

    [Fact]
    public void Name_Flat_KeepsTheOldRule()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(("_PROXY/1.mov", "1"), ProxyPaths.Pick(V("idle/1.mp4"), ProxyTree.Flat, used));
        Assert.Equal(("_PROXY/1_2.mov", "1_2"), ProxyPaths.Pick(V("listening/1.mp4"), ProxyTree.Flat, used));
    }

    [Fact]
    public void Detect_FlatOnlyWhenAnEarlierProxyOfASubfolderClipSitsAtTheTop()
    {
        Assert.Equal(ProxyTree.Mirror, ProxyPaths.Detect([]));
        // Only clips at the card's root: both layouts look the same — mirror.
        Assert.Equal(ProxyTree.Mirror, ProxyPaths.Detect([Done("1.mp4", "_PROXY/1.mov")]));
        // No proxies at all (analysis only, skipped): mirror.
        Assert.Equal(ProxyTree.Mirror, ProxyPaths.Detect([Done("idle/1.mp4", null)]));
        // An older backup (Windows 0.5.0, the Mac): idle\1.mp4 → _PROXY\1.mov.
        Assert.Equal(ProxyTree.Flat, ProxyPaths.Detect([Done("idle/1.mp4", "_PROXY/1.mov"), Done("listening/1.mp4", "_PROXY/1_2.mov")]));
        Assert.Equal(ProxyTree.Mirror, ProxyPaths.Detect([Done("idle/1.mp4", "_PROXY/idle/1.mov")]));
    }

    [Fact]
    public void Existing_EveryMovUnderProxy_AsFolderAndName()
    {
        using var dir = new TempDir();
        var a = dir.Sub("a");
        var b = dir.Sub("b");
        TestFs.WriteRandom(Path.Combine(a, "_PROXY", "idle", "1.mov"), 10, 1);
        TestFs.WriteRandom(Path.Combine(a, "_PROXY", "C0001.mov"), 10, 2);
        TestFs.WriteRandom(Path.Combine(b, "_PROXY", "listening", "deep", "2.MOV"), 10, 3);
        TestFs.WriteRandom(Path.Combine(b, "_PROXY", "listening", ".2.mov.part"), 10, 4);

        var keys = ProxyPaths.Existing([a, b, Path.Combine(dir.Path, "gone")]).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(["C0001", "idle/1", "listening/deep/2"], keys);
    }
}
