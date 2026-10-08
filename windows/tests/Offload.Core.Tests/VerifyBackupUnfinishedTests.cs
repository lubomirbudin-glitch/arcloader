using System.Text.Json.Nodes;
using Offload.Cli;
using Offload.Core.Desktop;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

/// Plan 10, carried M-c: "Ověřit zálohu" on a backup whose newest generation is unfinished (verifyPending: the app ended
/// while verifying with the proxies; verifyIncomplete: the results are not written on every destination) never says
/// "vše v pořádku" — a warning in Czech that points to „Dokončit přerušenou zálohu“, in the GUI, the CLI and its report.
[Collection("Console")]
public class VerifyBackupUnfinishedTests
{
    static string Backup(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 50_000, 1);
        TestFs.WriteRandom(Path.Combine(card, "SOUND", "A001.wav"), 20_000, 2);
        var engine = new OffloadEngine(new OffloadOptions { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true });
        return engine.Run(engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-08")).Destinations[0].CardPath;
    }

    /// The report of generation 1 says `flag` — as a copy the app never finished verifying leaves it.
    static void Flag(string folder, string flag, bool on = true)
    {
        var path = Path.Combine(folder, "_REPORT", "offload_report.json");
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (on) json[flag] = true;
        else json.Remove(flag);
        File.WriteAllText(path, json.ToJsonString());
    }

    static (int Exit, string Out) Cli(string folder)
    {
        var o = Console.Out;
        var e = Console.Error;
        var so = new StringWriter();
        Console.SetOut(so);
        Console.SetError(new StringWriter());
        try { return (VerifyCommand.Create().Parse([folder]).Invoke(), so.ToString()); }
        finally
        {
            Console.SetOut(o);
            Console.SetError(e);
        }
    }

    static void AssertUnfinished(VerifyResult r, string folder)
    {
        Assert.True(r.Unfinished);
        var (text, tone) = VerifyTexts.Summary(r);
        Assert.Equal(VerifyTone.Warning, tone);
        Assert.Contains("Dokončit přerušenou zálohu", text);
        Assert.DoesNotContain("vše v pořádku", VerifyTexts.CliSummary(r));
        Assert.Contains("Dokončit přerušenou zálohu", VerifyTexts.CliSummary(r));
        var html = VerifyReport.Html(r);
        Assert.Contains("NEDOKONČENO", html);
        Assert.DoesNotContain(">OVĚŘENO<", html);
        var done = new VerifyJobStatus(VerifyJobState.Done, null, r, null, null);
        Assert.Equal(("Nedokončeno", CardTone.Warning), (VerifyJobView.StatusText(done), VerifyJobView.Tone(done)));
        var (exit, output) = Cli(folder);
        Assert.Equal(1, exit);   // not a backup the card may be formatted after
        Assert.DoesNotContain("vše v pořádku", output);
    }

    [Theory]
    [InlineData("verifyPending")]
    [InlineData("verifyIncomplete")]
    public void NewestGenerationUnfinished_WarnsAndPointsToDokoncit(string flag)
    {
        using var dir = new TempDir();
        var folder = Backup(dir);
        var good = BackupVerifier.Verify(folder, chunkSize: 4096);
        Assert.False(good.Unfinished);
        Assert.Equal(VerifyTone.Ok, VerifyTexts.Summary(good).Tone);
        Assert.EndsWith("vše v pořádku.", VerifyTexts.CliSummary(good));
        Assert.Equal(0, Cli(folder).Exit);

        Flag(folder, flag);
        var r = BackupVerifier.Verify(folder, chunkSize: 4096);
        Assert.Equal(2, r.Ok);   // the files the record has are still read and compared
        AssertUnfinished(r, folder);

        Flag(folder, flag, on: false);
        Assert.False(BackupVerifier.Verify(folder, chunkSize: 4096).Unfinished);
    }

    // Generation 1 still verifying has neither an ASC MHL nor hash lists yet (they are written with the results): the
    // same warning, not "no hash record".
    [Fact]
    public void FirstGenerationPending_NoRecordYet_SameWarning()
    {
        using var dir = new TempDir();
        var folder = Backup(dir);
        Directory.Delete(Path.Combine(folder, "ascmhl"), recursive: true);
        foreach (var list in Directory.GetFiles(Path.Combine(folder, "_REPORT")).Where(f => f.EndsWith(".xxh64") || f.EndsWith(".md5"))) File.Delete(list);
        Assert.Throws<OffloadError>(() => BackupVerifier.Verify(folder, chunkSize: 4096));   // a folder without any record, as before

        Flag(folder, "verifyPending");
        var r = BackupVerifier.Verify(folder, chunkSize: 4096);
        Assert.Equal((0, 0), (r.Ok, r.Changed.Count + r.Missing.Count + r.Unreadable.Count));
        AssertUnfinished(r, folder);
    }
}
