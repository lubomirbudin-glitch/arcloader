using System.Text.Json;
using Offload.Core.Desktop;
using Offload.Core.Updates;

namespace Offload.Core.Tests;

/// Spec 4.4, R21: a manifest from a URL or a file, at most once in 24 h, never a download. A local HTTP server and temp
/// files only — and a state file in a temp folder, never %APPDATA%.
public class UpdateCheckerTests
{
    static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("0.4.0", "0.3.0-win", true)]
    [InlineData("0.10.0", "0.9.0", true)]
    [InlineData("0.9.0", "0.10.0-win", false)]
    [InlineData("1.0.0", "0.99.99", true)]
    [InlineData("0.4.0", "0.4.0-win", false)]
    [InlineData("0.4.0-beta", "0.4.0", false)]
    [InlineData("0.4.1", "0.4.0-win", true)]
    [InlineData("0.4", "0.3.9", true)]
    [InlineData("x.y", "0.3.0", false)]
    [InlineData("0.4.0", "garbage", false)]
    public void IsNewer_Table(string candidate, string current, bool newer) => Assert.Equal(newer, UpdateChecker.IsNewer(candidate, current));

    static string Manifest(TempDir dir, string json)
    {
        var path = Path.Combine(dir.Path, "offload-update.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task FileManifest_NewVersion_StateWritten()
    {
        using var dir = new TempDir();
        var state = Path.Combine(dir.Path, "state", "update-check.json");
        var source = Manifest(dir, """{"version":"0.5.0","url":"\\\\NAS\\Offload\\","notes":"Denní report, MD5…"}""");

        var r = await UpdateChecker.CheckAsync(source, "0.4.0-win", Now, state);

        Assert.Equal(UpdateStatus.NewVersion, r.Status);
        Assert.Equal(new UpdateInfo("0.5.0", @"\\NAS\Offload\", "Denní report, MD5…"), r.Info);
        Assert.Equal("Je k dispozici verze 0.5.0 (máte 0.4.0).", r.Text("0.4.0-win"));
        using var doc = JsonDocument.Parse(File.ReadAllText(state));
        Assert.Equal(Now, doc.RootElement.GetProperty("lastCheck").GetDateTime().ToUniversalTime());
        Assert.Equal("0.5.0", doc.RootElement.GetProperty("lastVersion").GetString());
    }

    // At most once in 24 h: within it the manifest is not read, the remembered newer version still shows; after it, or
    // with "Zkontrolovat teď", or when the clock went back, it is read again.
    [Fact]
    public async Task OncePerDay_FakeClock_ClockBack_Force()
    {
        using var dir = new TempDir();
        var state = Path.Combine(dir.Path, "update-check.json");
        var source = Manifest(dir, """{"version":"0.5.0"}""");
        Assert.Equal(UpdateStatus.NewVersion, (await UpdateChecker.CheckAsync(source, "0.4.0", Now, state)).Status);
        File.WriteAllText(source, """{"version":"0.6.0"}""");

        var cached = await UpdateChecker.CheckAsync(source, "0.4.0", Now.AddHours(23), state);
        Assert.Equal((UpdateStatus.NewVersion, "0.5.0"), (cached.Status, cached.Info!.Version));   // from the state, not read
        Assert.Equal(UpdateStatus.NotDue, (await UpdateChecker.CheckAsync(source, "0.5.0", Now.AddHours(23), state)).Status);

        Assert.Equal("0.6.0", (await UpdateChecker.CheckAsync(source, "0.4.0", Now.AddHours(25), state)).Info!.Version);
        File.WriteAllText(source, """{"version":"0.7.0"}""");
        Assert.Equal("0.7.0", (await UpdateChecker.CheckAsync(source, "0.4.0", Now.AddHours(26), state, force: true)).Info!.Version);
        File.WriteAllText(source, """{"version":"0.8.0"}""");
        Assert.Equal("0.8.0", (await UpdateChecker.CheckAsync(source, "0.4.0", Now.AddDays(-3), state)).Info!.Version);   // clock went back
    }

    // M5: a result remembered for another address is not valid after the address changed.
    [Fact]
    public async Task ChangedSource_IsDueAgain()
    {
        using var dir = new TempDir();
        var state = Path.Combine(dir.Path, "update-check.json");
        var a = Manifest(dir, """{"version":"0.5.0"}""");
        var b = Path.Combine(dir.Path, "b.json");
        File.WriteAllText(b, """{"version":"0.9.0"}""");
        await UpdateChecker.CheckAsync(a, "0.4.0", Now, state);
        Assert.Equal("0.9.0", (await UpdateChecker.CheckAsync(b, "0.4.0", Now.AddHours(1), state)).Info!.Version);
        File.WriteAllText(b, """{"version":"0.9.5"}""");
        Assert.Equal("0.9.0", (await UpdateChecker.CheckAsync(b, "0.4.0", Now.AddHours(2), state)).Info!.Version);   // same address: remembered
    }

    [Fact]
    public async Task UpToDate_Off_Damaged()
    {
        using var dir = new TempDir();
        var state = Path.Combine(dir.Path, "update-check.json");
        var same = await UpdateChecker.CheckAsync(Manifest(dir, """{"version":"0.4.0"}"""), "0.4.0-win", Now, state, force: true);
        Assert.Equal((UpdateStatus.UpToDate, "Máte nejnovější verzi 0.4.0."), (same.Status, same.Text("0.4.0-win")));

        Assert.Equal(UpdateStatus.Off, (await UpdateChecker.CheckAsync(null, "0.4.0", Now, state)).Status);
        Assert.Equal(UpdateStatus.Off, (await UpdateChecker.CheckAsync("  ", "0.4.0", Now, state)).Status);

        var broken = await UpdateChecker.CheckAsync(Manifest(dir, "{\"version\":"), "0.4.0", Now, state, force: true);
        Assert.Equal(UpdateStatus.Failed, broken.Status);
        Assert.StartsWith("Manifest nejde načíst: poškozený JSON", broken.Text("0.4.0"));
        var noVersion = await UpdateChecker.CheckAsync(Manifest(dir, """{"version":"latest"}"""), "0.4.0", Now, state, force: true);
        Assert.Equal("Manifest nejde načíst: v manifestu chybí platná verze (MAJOR.MINOR.PATCH)", noVersion.Text("0.4.0"));
        var missing = await UpdateChecker.CheckAsync(Path.Combine(dir.Path, "none.json"), "0.4.0", Now, state, force: true);
        Assert.Equal(UpdateStatus.Failed, missing.Status);

        File.WriteAllText(state, "{oops");   // a damaged state file: simply checked again
        Assert.Equal(UpdateStatus.UpToDate, (await UpdateChecker.CheckAsync(Manifest(dir, "\uFEFF{\"version\":\"0.4.0\"}"), "0.4.0", Now, state)).Status);
    }

    // M5: a huge manifest is refused (file and HTTP), long notes are cut.
    [Fact]
    public async Task HugeManifest_Refused_LongNotesCut()
    {
        using var dir = new TempDir();
        var big = Manifest(dir, """{"version":"0.5.0"}""" + new string(' ', UpdateChecker.MaxManifestBytes));
        var r = await UpdateChecker.CheckAsync(big, "0.4.0", Now, Path.Combine(dir.Path, "a.json"));
        Assert.Equal((UpdateStatus.Failed, "Manifest nejde načíst: soubor je větší než 64 KB"), (r.Status, r.Text("0.4.0")));

        using var server = new LocalHttp(c => LocalHttp.Reply(c, 200, """{"version":"0.5.0"}""" + new string(' ', UpdateChecker.MaxManifestBytes)));
        var h = await UpdateChecker.CheckAsync(server.Url, "0.4.0", Now, Path.Combine(dir.Path, "b.json"));
        Assert.Equal((UpdateStatus.Failed, "odpověď je větší než 64 KB"), (h.Status, h.Error));

        var notes = new string('n', 5000);
        var cut = await UpdateChecker.CheckAsync(Manifest(dir, "{\"version\":\"0.5.0\",\"notes\":\"" + notes + "\"}"), "0.4.0", Now, Path.Combine(dir.Path, "c.json"), force: true);
        Assert.Equal(UpdateChecker.MaxNotesLength, cut.Info!.Notes!.Length);
    }

    [Fact]
    public async Task Http_Manifest_UserAgent_NoIdentifiers()
    {
        using var dir = new TempDir();
        using var server = new LocalHttp(c => LocalHttp.Reply(c, 200, """{"version":"0.5.0","url":"https://example.invalid/offload.zip"}"""));

        var r = await UpdateChecker.CheckAsync(server.Url + "offload-update.json", "0.4.0-win", Now, Path.Combine(dir.Path, "s.json"));

        Assert.Equal((UpdateStatus.NewVersion, "https://example.invalid/offload.zip"), (r.Status, r.Info!.Url));
        var req = Assert.Single(server.Requests);
        Assert.Equal(("GET", "/offload-update.json"), (req.Method, req.Path));
        Assert.Matches(@"^Offload/\d+\.\d+\.\d+$", req.UserAgent);
    }

    // Review focus 4: a server that does not answer, a 404 — Failed with the reason. Only the outcome is asserted
    // (no stopwatch): the limit is cancelled by a token, so there is no race with the clock.
    [Fact]
    public async Task Http_Silent_Or_404_Failed()
    {
        using var dir = new TempDir();
        using var silent = new LocalHttp(_ => Task.Delay(Timeout.Infinite));
        var r = await UpdateChecker.CheckAsync(silent.Url, "0.4.0", Now, Path.Combine(dir.Path, "a.json"), httpTimeout: TimeSpan.FromMilliseconds(300));
        Assert.Equal(UpdateStatus.Failed, r.Status);
        Assert.EndsWith("neodpověděl do 0.3 s", r.Error);

        using var missing = new LocalHttp(c => LocalHttp.Reply(c, 404, "no"));
        var nf = await UpdateChecker.CheckAsync(missing.Url, "0.4.0", Now, Path.Combine(dir.Path, "b.json"));
        Assert.StartsWith("server odpověděl 404", nf.Error);
    }

    // Review focus 4: a dead UNC path would block for tens of seconds — the file read gives up after its limit
    // (here 300 ms, against a path that does not answer: a named pipe that never writes). M6: the outcome does not
    // depend on whether the client connected within the limit, and nothing is Disconnect()ed.
    [Fact]
    public async Task File_ThatDoesNotAnswer_GivesUp()
    {
        using var dir = new TempDir();
        var pipeName = "offload-test-" + Guid.NewGuid().ToString("N");
        using var pipe = new System.IO.Pipes.NamedPipeServerStream(pipeName, System.IO.Pipes.PipeDirection.InOut, 1);
        _ = pipe.WaitForConnectionAsync();   // accepts, then never writes: a read blocks
        var r = await UpdateChecker.CheckAsync($@"\\.\pipe\{pipeName}", "0.4.0", Now, Path.Combine(dir.Path, "s.json"), fileTimeout: TimeSpan.FromMilliseconds(300));
        Assert.Equal(UpdateStatus.Failed, r.Status);
        Assert.EndsWith("neodpověděl do 0.3 s", r.Error);
    }

    // Ruling I5: "Otevřít" opens only an https URL, or an existing folder / zip through Explorer — never anything else.
    [Fact]
    public void UpdateLinks_Target_Table()
    {
        using var dir = new TempDir();
        var zip = Path.Combine(dir.Path, "Offload-win-x64-0.5.0.zip");
        File.WriteAllText(zip, "x");
        var exe = Path.Combine(dir.Path, "setup.exe");
        File.WriteAllText(exe, "x");
        var lnk = Path.Combine(dir.Path, "a.lnk");
        File.WriteAllText(lnk, "x");

        Assert.Equal((UpdateLinkKind.Web, "https://example.com/offload"), UpdateLinks.Target("https://example.com/offload"));
        Assert.Equal((UpdateLinkKind.Folder, dir.Path), UpdateLinks.Target(dir.Path));
        Assert.Equal((UpdateLinkKind.Folder, dir.Path), UpdateLinks.Target(new Uri(dir.Path).AbsoluteUri));
        Assert.Equal((UpdateLinkKind.File, zip), UpdateLinks.Target(zip));

        foreach (var bad in new[]
        {
            "http://example.com/offload", exe, lnk, @"C:\Windows\System32\cmd.exe", "ms-settings:network", "javascript:alert(1)",
            new Uri(exe).AbsoluteUri, "search-ms:query=x", "ftp://example.com/x", Path.Combine(dir.Path, "gone"), Path.Combine(dir.Path, "gone.zip"),
            "", "   ", null, "offload",
        })
            Assert.Equal((UpdateLinkKind.None, ""), UpdateLinks.Target(bad));
    }

    [Fact]
    public void Setting_Updates_GlobalAndKept()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, """{"updates":{"manifest":"  \\\\NAS\\Offload\\offload-update.json ","future":1}}""");
        var s = SettingsStore.Load(path);
        Assert.Equal(@"\\NAS\Offload\offload-update.json", s.Updates.Manifest);
        Projects.SaveAs(s, "Film X");                       // not part of a project
        SettingsStore.Save(s, path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(1, doc.RootElement.GetProperty("updates").GetProperty("future").GetInt32());
        Assert.False(doc.RootElement.GetProperty("projects")[0].TryGetProperty("updates", out _));
        Assert.Null(SettingsStore.Load(Path.Combine(dir.Path, "none.json")).Updates.Manifest);   // off by default
    }
}
