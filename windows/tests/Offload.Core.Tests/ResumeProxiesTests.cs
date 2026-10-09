using Offload.Core.Append;

namespace Offload.Core.Tests;

/// Plan 10, carried from tasks 12–14: "Dokončit přerušenou zálohu" after the app ended in stage 2 keeps every proxy the
/// interrupted run finished (complete, not a `.part`) — in the mirrored tree of plan 9, or flat in an older backup — and
/// renders only the missing ones under their own names: never `_2` beside its own proxy, never over a file.
public class ResumeProxiesTests
{
    static string Proxy(string cardPath, string clip) => Path.Combine(cardPath, "_PROXY", "CLIP", clip + ".mov");

    [FfmpegFact]
    public void CrashInStageTwo_Dokoncit_KeepsFinishedProxies_RendersOnlyTheMissing()
    {
        using var dir = new TempDir();
        var card = VerifyInProxyRulingsTests.Card(dir);
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "A001C002.mov"), seconds: 2, width: 640, height: 360);
        var job = new OffloadJob(VerifyInProxyRulingsTests.Config(card, [dir.Sub("d1"), dir.Sub("d2")], dir), VerifyInProxyRulingsTests.Options())
        {
            // The app "ends" after the proxies, before the results are written: nothing committed, every folder pending.
            BeforeCommit = _ => throw new IOException("the app ended"),
        };
        job.Run();
        Assert.False(job.Status().SafeToFormat);
        var paths = job.Plan!.CardPaths;
        Assert.All(paths, p => Assert.True(File.Exists(Proxy(p, "A001C001")) && File.Exists(Proxy(p, "A001C002"))));   // copied to both disks
        // The second proxy was being rendered when the app ended: only its part is there.
        foreach (var p in paths)
        {
            File.Delete(Proxy(p, "A001C002"));
            File.WriteAllBytes(Path.Combine(p, "_PROXY", "CLIP", ".A001C002.mov.part"), [1, 2, 3]);
        }
        var kept = paths.Select(p => Proxy(p, "A001C001")).ToList();
        var keptBytes = kept.Select(File.ReadAllBytes).ToList();
        var keptTimes = kept.Select(File.GetLastWriteTimeUtc).ToList();
        Assert.NotNull(ResumePlanner.Find(paths));

        var config = VerifyInProxyRulingsTests.Config(card, [], dir);
        config.AppendInto = [.. paths];
        var resume = new OffloadJob(config, VerifyInProxyRulingsTests.Options());
        resume.Run();

        Assert.True(resume.Plan!.Resuming);
        Assert.True(resume.Status().SafeToFormat);   // the kept proxy's clip is verified by the fill-in read
        Assert.Equal(keptBytes, kept.Select(File.ReadAllBytes).ToList());   // never rendered or copied over again
        Assert.Equal(keptTimes, kept.Select(File.GetLastWriteTimeUtc).ToList());
        foreach (var p in paths)
        {
            Assert.True(File.Exists(Proxy(p, "A001C002")));   // rendered again under its own name (and copied)
            Assert.Empty(Directory.GetFiles(p, "*_2.mov", SearchOption.AllDirectories));
        }
        // The stale parts went the way every part goes: the render's became the proxy, the copy's the copy.
        Assert.All(paths, p => Assert.False(File.Exists(Path.Combine(p, "_PROXY", "CLIP", ".A001C002.mov.part"))));
        var clips = resume.Report!.Clips.Where(c => c.Clip.Name.StartsWith("A001C00", StringComparison.Ordinal)).OrderBy(c => c.Clip.Name).ToList();
        Assert.Equal([("_PROXY/CLIP/A001C001.mov", ProxyStatus.Done), ("_PROXY/CLIP/A001C002.mov", ProxyStatus.Done)],
            clips.Select(c => (c.ProxyPath, c.ProxyStatus)));
        Assert.Equal(Msg.ProxyKeptOnResume, clips[0].ProxyNote);   // kept, and the report says so
        Assert.Null(clips[1].ProxyNote);
        Assert.NotNull(clips[1].Content);   // the clip rendered now got its content check
        Assert.All(paths, p => Assert.True(CardRecord.Load(p).Verified));
    }

    // An older flat backup (Windows 0.5.0, the Mac): generation 1's proxy "A001C001" is at the top of _PROXY; generation 2
    // (unfinished) has a clip of the same name in another folder — its own proxy is "A001C001_2", made by the run that
    // ended. A resume finds it there (complete), never names a third one; a part only is not a finished proxy.
    [Fact]
    public void Flat_ResumeFindsItsOwnProxy_TheRecordedOnesStayTaken()
    {
        using var dir = new TempDir();
        var folder = dir.Sub(@"d1\2026-10-08\A001");
        var first = Reports.Sample(folder);
        first.Generation = 1;
        first.Clips = [Entry("A001C001", "CLIP/A001C001.mov", "_PROXY/A001C001.mov")];
        var second = Reports.Sample(folder, verified: false);
        second.Generation = 2;
        second.VerifyPending = true;
        second.Clips = [Entry("A001C001", "CLIP2/A001C001.mov", null)];
        Directory.CreateDirectory(Path.Combine(folder, "_REPORT"));
        File.WriteAllText(Path.Combine(folder, "_REPORT", OffloadReport.JsonName(1)), first.ToJson());
        File.WriteAllText(Path.Combine(folder, "_REPORT", OffloadReport.JsonName(2)), second.ToJson());
        Directory.CreateDirectory(Path.Combine(folder, "_PROXY"));
        File.WriteAllBytes(Path.Combine(folder, "_PROXY", "A001C001.mov"), [1]);

        Assert.Equal(ProxyTree.Flat, ProxyPaths.Of([folder]));
        Assert.Equal(["A001C001"], ProxyPaths.Recorded([folder], generation: 2));
        Assert.Empty(ProxyPaths.Recorded([folder], generation: 1));
        var used = new HashSet<string>(ProxyPaths.Recorded([folder], generation: 2), StringComparer.OrdinalIgnoreCase);
        var (rel, name) = ProxyPaths.Pick(second.Clips[0].Clip, ProxyTree.Flat, used);
        Assert.Equal(("_PROXY/A001C001_2.mov", "A001C001_2"), (rel, name));

        File.WriteAllBytes(Path.Combine(folder, "_PROXY", ".A001C001_2.mov.part"), [1]);
        Assert.Null(ProxyPaths.Finished([folder], rel, prefer: 0));   // only a part: rendered again
        File.WriteAllBytes(Path.Combine(folder, "_PROXY", "A001C001_2.mov"), [1, 2]);
        Assert.Equal(0, ProxyPaths.Finished([folder], rel, prefer: 0));
        Assert.Equal(1, ProxyPaths.Finished([dir.Sub("elsewhere"), folder], rel, prefer: 0));   // on another destination: that one
        File.WriteAllBytes(Path.Combine(folder, "_PROXY", "A001C001_2.mov"), []);
        Assert.Null(ProxyPaths.Finished([folder], rel, prefer: 0));   // empty: never a finished proxy
    }

    static ClipEntry Entry(string name, string path, string? proxy) => new()
    {
        Clip = new Clip { Name = name, Path = path, Segments = [path], Format = ClipFormat.Video },
        ProxyStatus = proxy != null ? ProxyStatus.Done : ProxyStatus.None,
        ProxyPath = proxy,
    };
}
