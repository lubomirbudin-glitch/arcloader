using System.Text.Json;
using Offload.Cli;
using Offload.Core.Desktop;
using Offload.Core.Notify;

namespace Offload.Core.Tests;

/// Spec 4.3 CLI: `--ntfy <url>` or the project's ntfy — the result goes to a local HTTP server here, never the internet.
[Collection("Console")]
public class CliNtfyTests
{
    static (int Exit, string Out, string Err) Run(string? settingsDir, params string[] args)
    {
        var o = Console.Out;
        var e = Console.Error;
        var so = new StringWriter();
        var se = new StringWriter();
        var oldFactory = CopyCommand.JobFactory;
        var oldPath = CliProject.SettingsPath;
        Console.SetOut(so);
        Console.SetError(se);
        if (settingsDir != null) CliProject.SettingsPath = () => Path.Combine(settingsDir, "settings.json");
        CopyCommand.JobFactory = c => new OffloadJob(c, AppendFixtures.Options());
        try { return (CopyCommand.Create().Parse(args).Invoke(), so.ToString(), se.ToString()); }
        finally
        {
            Console.SetOut(o);
            Console.SetError(e);
            CopyCommand.JobFactory = oldFactory;
            CliProject.SettingsPath = oldPath;
        }
    }

    static string Card(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 1_000, 1);
        return card;
    }

    static JsonElement Body(LocalHttp server) => JsonDocument.Parse(Assert.Single(server.Requests).Body).RootElement.Clone();

    [Fact]
    public void Ntfy_AfterTheCopy_TopicFromTheUrl()
    {
        using var dir = new TempDir();
        using var server = new LocalHttp();

        var r = Run(null, Card(dir), "-d", dir.Sub("d1"), "--no-history", "--no-analyze", "--ntfy", server.Url + "kanal-1");

        Assert.Equal(0, r.Exit);
        var body = Body(server);
        Assert.Equal("kanal-1", body.GetProperty("topic").GetString());
        Assert.Equal("Offload: A001 hotovo", body.GetProperty("title").GetString());
        Assert.Equal("Ověřeno na 1 disku — kartu lze formátovat.", body.GetProperty("message").GetString());
        Assert.Contains("ntfy: sent", r.Err);
    }

    // Final fix 1: an append notifies the EARLIER backup's project (Film X), not the --project given (Reklama Y); an
    // explicit --ntfy still wins.
    [Fact]
    public void Append_NotifiesTheEarlierProject_NotTheOneGiven_NtfyWins()
    {
        using var dir = new TempDir();
        using var film = new LocalHttp();
        using var reklama = new LocalHttp();
        using var explicitly = new LocalHttp();
        var folder = dir.Sub("appdata");
        var s = new AppSettings();
        var x = Projects.SaveAs(s, "Film X");
        x.Notify.Ntfy = new NtfySettings { Enabled = true, Server = film.Url.TrimEnd('/'), Topic = "film-x" };
        var y = Projects.SaveAs(s, "Reklama Y");
        y.Notify.Ntfy = new NtfySettings { Enabled = true, Server = reklama.Url.TrimEnd('/'), Topic = "reklama-y" };
        Projects.Activate(s, null);
        SettingsStore.Save(s, Path.Combine(folder, "settings.json"));
        var card = Card(dir);
        var d1 = dir.Sub("d1");
        Assert.Equal(0, Run(folder, card, "--project", "Film X", "-d", d1, "--date", "2026-10-07", "--no-history", "--no-analyze").Exit);
        Assert.Single(film.Requests);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), 1_000, 2);
        var into = Path.Combine(d1, "2026-10-07", "A001");

        var r = RunAppend(folder, card, "--into", into, "--project", "Reklama Y", "--no-history", "--no-analyze");

        Assert.Equal(0, r.Exit);
        Assert.Contains("Oznámení podle projektu dřívější zálohy: Film X", r.Out);
        Assert.Equal(2, film.Requests.Count);
        Assert.Empty(reklama.Requests);

        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C003.mov"), 1_000, 3);
        Assert.Equal(0, RunAppend(folder, card, "--into", into, "--project", "Reklama Y", "--no-history", "--no-analyze", "--ntfy", explicitly.Url + "primo").Exit);
        Assert.Equal("primo", Body(explicitly).GetProperty("topic").GetString());
        Assert.Equal(2, film.Requests.Count);
        Assert.Empty(reklama.Requests);
    }

    static (int Exit, string Out, string Err) RunAppend(string settingsDir, params string[] args)
    {
        var o = Console.Out;
        var e = Console.Error;
        var so = new StringWriter();
        var se = new StringWriter();
        var oldFactory = CopyCommand.JobFactory;
        var oldPath = CliProject.SettingsPath;
        Console.SetOut(so);
        Console.SetError(se);
        CliProject.SettingsPath = () => Path.Combine(settingsDir, "settings.json");
        CopyCommand.JobFactory = c => new OffloadJob(c, AppendFixtures.Options());
        try { return (AppendCommand.Create().Parse(args).Invoke(), so.ToString(), se.ToString()); }
        finally
        {
            Console.SetOut(o);
            Console.SetError(e);
            CopyCommand.JobFactory = oldFactory;
            CliProject.SettingsPath = oldPath;
        }
    }

    [Fact]
    public void BadUrl_Exit1_Czech_NothingCopied()
    {
        using var dir = new TempDir();
        var d1 = dir.Sub("d1");
        var r = Run(null, Card(dir), "-d", d1, "--no-history", "--ntfy", "https://ntfy.sh/kanál");
        Assert.Equal(1, r.Exit);
        Assert.Contains("Chyba: Adresa ntfy „https://ntfy.sh/kanál“ nejde použít", r.Err);
        Assert.Empty(Directory.GetDirectories(d1));
    }

    // A refusal before the copy is announced too (a script nobody watches), without the path in the text.
    [Fact]
    public void RefusedBeforeTheCopy_Announced()
    {
        using var dir = new TempDir();
        using var server = new LocalHttp();
        var r = Run(null, Card(dir), "-d", Path.Combine(dir.Path, "missing"), "--no-history", "--ntfy", server.Url + "k");
        Assert.Equal(1, r.Exit);
        var body = Body(server);
        Assert.Equal("Offload: A001 — CHYBA", body.GetProperty("title").GetString());
        Assert.Equal("Chyba před kopírováním — karta NENÍ zálohovaná.", body.GetProperty("message").GetString());
        Assert.Equal(5, body.GetProperty("priority").GetInt32());
    }

    // A refusal before the plan exists: the card is named like the engine names it — never the source path.
    [Fact]
    public void RefusedBeforeThePlan_CardNamedNotPathed()
    {
        using var dir = new TempDir();
        using var server = new LocalHttp();
        var r = Run(null, Path.Combine(dir.Path, "B002"), "-d", dir.Sub("d1"), "--no-history", "--ntfy", server.Url + "k");   // no such source
        Assert.Equal(1, r.Exit);
        Assert.Equal("Offload: B002 — CHYBA", Body(server).GetProperty("title").GetString());
    }

    // --project with ntfy on sends there; --ntfy on the command line wins; without either nothing is sent.
    [Fact]
    public void ProjectNtfy_AndTheOptionWins()
    {
        using var dir = new TempDir();
        using var project = new LocalHttp();
        using var written = new LocalHttp();
        var folder = dir.Sub("appdata");
        var s = new AppSettings();
        var p = Projects.SaveAs(s, "Film X");
        p.Notify.Ntfy = new NtfySettings { Enabled = true, Server = project.Url, Topic = "film-x" };
        SettingsStore.Save(s, Path.Combine(folder, "settings.json"));

        Assert.Equal(0, Run(folder, Card(dir), "--project", "Film X", "-d", dir.Sub("d1"), "--no-history", "--no-analyze").Exit);
        Assert.Equal("film-x", Body(project).GetProperty("topic").GetString());

        Assert.Equal(0, Run(folder, Card(dir), "--project", "Film X", "-d", dir.Sub("d2"), "--no-history", "--no-analyze", "--ntfy", written.Url + "jine").Exit);
        Assert.Single(project.Requests);
        Assert.Equal("jine", Body(written).GetProperty("topic").GetString());

        Assert.Equal(0, Run(folder, Card(dir), "-d", dir.Sub("d3"), "--no-history", "--no-analyze").Exit);
        Assert.Single(project.Requests);
        Assert.Single(written.Requests);
    }

    // Spec 5.6: no path of the job — source, destination, clip — in any ntfy payload, success or failure.
    [Fact]
    public void NoPathOfTheJob_InAnyPayload()
    {
        using var dir = new TempDir();
        using var server = new LocalHttp();
        var card = dir.Sub("karta-zdroj", "A001");   // the card is named A001; its parent folder must not leak
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "TAJNYKLIP_C001.mov"), 1_000, 1);
        var d1 = dir.Sub("cil-tajny");

        Assert.Equal(0, Run(null, card, "-d", d1, "--no-history", "--no-analyze", "--ntfy", server.Url + "k").Exit);
        Assert.Equal(1, Run(null, card, "-d", Path.Combine(dir.Path, "cil-chybi"), "--no-history", "--ntfy", server.Url + "k").Exit);
        Assert.Equal(1, Run(null, Path.Combine(dir.Path, "zdroj-chybi", "B002"), "-d", d1, "--no-history", "--ntfy", server.Url + "k").Exit);

        Assert.Equal(3, server.Requests.Count);
        foreach (var req in server.Requests)
        {
            var text = System.Text.Encoding.UTF8.GetString(req.Body);
            foreach (var forbidden in new[] { dir.Path, "karta-zdroj", "cil-tajny", "cil-chybi", "zdroj-chybi", "TAJNYKLIP", ".mov", "offload-tests" })
                Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\\", text);   // no Windows path separator at all (JSON-escaped "\\")
        }
    }

    // The same for outcomes whose Windows text names a path (error / warning): the phone gets the fixed text only.
    [Fact]
    public async Task OutcomesWithPathsInTheirWindowsText_SendNoPath()
    {
        using var server = new LocalHttp();
        var settings = new NtfySettings { Enabled = true, Server = server.Url, Topic = "k" };
        var paths = @"E:\Karta\CLIP\A001C001.mov";
        foreach (var (status, report) in new (JobStatus, OffloadReport?)[]
        {
            (new JobStatus { Phase = JobPhase.Failed, Error = "Cíl nenalezen: " + paths }, null),
            (new JobStatus { Phase = JobPhase.Done, Warning = "kopie do " + paths + " selhala" }, Reports.Sample(@"D:\Zaloha\A001")),
            (new JobStatus { Phase = JobPhase.Done }, Reports.Sample(@"D:\Zaloha\A001", destError: paths)),
        })
            await NtfyClient.SendOutcomeAsync(settings, JobOutcomes.Final("A001", status, report), null);

        Assert.Equal(3, server.Requests.Count);
        foreach (var req in server.Requests)
        {
            var text = System.Text.Encoding.UTF8.GetString(req.Body);
            foreach (var forbidden in new[] { "Karta", "CLIP", "A001C001", ".mov", "Zaloha", "E:", "D:" })
                Assert.DoesNotContain(forbidden, text);
        }
    }

    // A refusal thrown while the options are built (here: append of a card that is not in the history) is announced too,
    // with the card name only.
    [Fact]
    public void RefusalWhileBuildingTheOptions_Announced_CardNameOnly()
    {
        using var dir = new TempDir();
        using var server = new LocalHttp();
        var card = dir.Sub("tajny-rodic", "A001");
        var o = Console.Out;
        var e = Console.Error;
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());
        int exit;
        try { exit = AppendCommand.Create().Parse([card, "--no-history", "--ntfy", server.Url + "k"]).Invoke(); }
        finally { Console.SetOut(o); Console.SetError(e); }

        Assert.Equal(1, exit);
        var req = Assert.Single(server.Requests);
        var text = System.Text.Encoding.UTF8.GetString(req.Body);
        Assert.Contains("Offload: A001 — CHYBA", text);
        Assert.DoesNotContain("tajny-rodic", text);
    }

    [Fact]
    public void DialogCheck_ServerAndTopic_OnlyWhenOn()
    {
        Assert.Null(NtfyClient.SettingsProblem(new NtfySettings { Enabled = false, Topic = "" }));
        Assert.Equal("Téma není vyplněné.", NtfyClient.SettingsProblem(new NtfySettings { Enabled = true }));
        Assert.Equal("Adresa serveru „ntfy.sh“ není http(s) adresa.", NtfyClient.SettingsProblem(new NtfySettings { Enabled = true, Server = "ntfy.sh", Topic = "a" }));
        Assert.Null(NtfyClient.SettingsProblem(new NtfySettings { Enabled = true, Topic = "a_b-1" }));
    }

    // The queue keeps a copy (spec 4.1): a job started in a project announces to that project's topic even after a switch.
    [Fact]
    public void JobConfig_NotifyIsTheJobsOwnCopy()
    {
        var s = new AppSettings();
        var p = Projects.SaveAs(s, "Film X");
        p.Notify.Ntfy.Topic = "film-x";
        var config = new JobConfig(@"E:\", []) { Notify = SettingsStore.Clone(s.Active.Notify) };
        Projects.Activate(s, null);
        s.Active.Notify.Ntfy.Topic = "jine";
        p.Notify.Ntfy.Topic = "zmeneno";
        Assert.Equal("film-x", config.Notify.Ntfy.Topic);
        Assert.True(new JobConfig(@"E:\", []).Notify.Windows);
        Assert.False(new JobConfig(@"E:\", []).Notify.Ntfy.Enabled);
    }
}
