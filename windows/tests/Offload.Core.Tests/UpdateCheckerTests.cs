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
        var source = Manifest(dir, """{"version":"0.5.0","url":"\\\\NAS\\Offload\\","notes":"Day report, MD5…"}""");

        var r = await UpdateChecker.CheckAsync(source, "0.4.0-win", Now, state);

        Assert.Equal(UpdateStatus.NewVersion, r.Status);
        Assert.Equal(new UpdateInfo("0.5.0", @"\\NAS\Offload\", "Day report, MD5…"), r.Info);
        Assert.Equal("Version 0.5.0 is available (you have 0.4.0).", r.Text("0.4.0-win"));
        using var doc = JsonDocument.Parse(File.ReadAllText(state));
        Assert.Equal(Now, doc.RootElement.GetProperty("lastCheck").GetDateTime().ToUniversalTime());
        Assert.Equal("0.5.0", doc.RootElement.GetProperty("lastVersion").GetString());
    }

    // At most once in 24 h: within it the manifest is not read, the remembered newer version still shows; after it, or
    // with "Check now", or when the clock went back, it is read again. Every read waits Patient: this test is about the
    // verdicts (the flake: a later read under load hit the 5 s default and came back Failed — Info null).
    [Fact]
    public async Task OncePerDay_FakeClock_ClockBack_Force()
    {
        using var dir = new TempDir();
        var state = Path.Combine(dir.Path, "update-check.json");
        var source = Manifest(dir, """{"version":"0.5.0"}""");
        Assert.Equal(UpdateStatus.NewVersion, (await Check(source, "0.4.0", Now, state)).Status);
        File.WriteAllText(source, """{"version":"0.6.0"}""");

        var cached = await Check(source, "0.4.0", Now.AddHours(23), state);
        Assert.Equal((UpdateStatus.NewVersion, "0.5.0"), (cached.Status, cached.Info?.Version));   // from the state, not read
        Assert.Equal(UpdateStatus.NotDue, (await Check(source, "0.5.0", Now.AddHours(23), state)).Status);

        Assert.Equal("0.6.0", Version(await Check(source, "0.4.0", Now.AddHours(25), state)));
        File.WriteAllText(source, """{"version":"0.7.0"}""");
        Assert.Equal("0.7.0", Version(await Check(source, "0.4.0", Now.AddHours(26), state, force: true)));
        File.WriteAllText(source, """{"version":"0.8.0"}""");
        Assert.Equal("0.8.0", Version(await Check(source, "0.4.0", Now.AddDays(-3), state)));   // clock went back
    }

    /// A check that waits long (the verdict, not the limit, is tested).
    static Task<UpdateResult> Check(string source, string current, DateTime now, string state, bool force = false) =>
        UpdateChecker.CheckAsync(source, current, now, state, force, httpTimeout: Patient, fileTimeout: Patient);

    /// The version found — or the failure, so a failed check names its reason instead of a NullReferenceException.
    static string Version(UpdateResult r) => r.Info?.Version ?? $"{r.Status}: {r.Error}";

    // M5: a result remembered for another address is not valid after the address changed.
    [Fact]
    public async Task ChangedSource_IsDueAgain()
    {
        using var dir = new TempDir();
        var state = Path.Combine(dir.Path, "update-check.json");
        var a = Manifest(dir, """{"version":"0.5.0"}""");
        var b = Path.Combine(dir.Path, "b.json");
        File.WriteAllText(b, """{"version":"0.9.0"}""");
        await Check(a, "0.4.0", Now, state);
        Assert.Equal("0.9.0", Version(await Check(b, "0.4.0", Now.AddHours(1), state)));
        File.WriteAllText(b, """{"version":"0.9.5"}""");
        Assert.Equal("0.9.0", Version(await Check(b, "0.4.0", Now.AddHours(2), state)));   // same address: remembered
    }

    /// Final review, item 8: on a loaded machine (the full suite) the file read waits for a pool thread, maybe past the
    /// 5 s default — these tests are about the verdicts, not the limit (the pipe test is), so they wait long.
    static readonly TimeSpan Patient = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task UpToDate_Off_Damaged()
    {
        using var dir = new TempDir();
        var state = Path.Combine(dir.Path, "update-check.json");
        var same = await UpdateChecker.CheckAsync(Manifest(dir, """{"version":"0.4.0"}"""), "0.4.0-win", Now, state, force: true, fileTimeout: Patient, httpTimeout: Patient);
        Assert.Equal((UpdateStatus.UpToDate, "You have the latest version 0.4.0."), (same.Status, same.Text("0.4.0-win")));

        Assert.Equal(UpdateStatus.Off, (await UpdateChecker.CheckAsync(null, "0.4.0", Now, state, fileTimeout: Patient, httpTimeout: Patient)).Status);
        Assert.Equal(UpdateStatus.Off, (await UpdateChecker.CheckAsync("  ", "0.4.0", Now, state, fileTimeout: Patient, httpTimeout: Patient)).Status);

        var broken = await UpdateChecker.CheckAsync(Manifest(dir, "{\"version\":"), "0.4.0", Now, state, force: true, fileTimeout: Patient, httpTimeout: Patient);
        Assert.Equal(UpdateStatus.Failed, broken.Status);
        Assert.StartsWith("Cannot read the manifest: damaged JSON", broken.Text("0.4.0"));
        var noVersion = await UpdateChecker.CheckAsync(Manifest(dir, """{"version":"latest"}"""), "0.4.0", Now, state, force: true, fileTimeout: Patient, httpTimeout: Patient);
        Assert.Equal("Cannot read the manifest: the manifest has no valid version (MAJOR.MINOR.PATCH)", noVersion.Text("0.4.0"));
        var missing = await UpdateChecker.CheckAsync(Path.Combine(dir.Path, "none.json"), "0.4.0", Now, state, force: true, fileTimeout: Patient, httpTimeout: Patient);
        Assert.Equal(UpdateStatus.Failed, missing.Status);

        File.WriteAllText(state, "{oops");   // a damaged state file: simply checked again
        Assert.Equal(UpdateStatus.UpToDate, (await UpdateChecker.CheckAsync(Manifest(dir, "\uFEFF{\"version\":\"0.4.0\"}"), "0.4.0", Now, state, fileTimeout: Patient, httpTimeout: Patient)).Status);
    }

    // M5: a huge manifest is refused (file and HTTP), long notes are cut.
    [Fact]
    public async Task HugeManifest_Refused_LongNotesCut()
    {
        using var dir = new TempDir();
        var big = Manifest(dir, """{"version":"0.5.0"}""" + new string(' ', UpdateChecker.MaxManifestBytes));
        var r = await UpdateChecker.CheckAsync(big, "0.4.0", Now, Path.Combine(dir.Path, "a.json"), fileTimeout: Patient, httpTimeout: Patient);
        Assert.Equal((UpdateStatus.Failed, "Cannot read the manifest: the file is larger than 64 KB"), (r.Status, r.Text("0.4.0")));

        using var server = new LocalHttp(c => LocalHttp.Reply(c, 200, """{"version":"0.5.0"}""" + new string(' ', UpdateChecker.MaxManifestBytes)));
        var h = await UpdateChecker.CheckAsync(server.Url, "0.4.0", Now, Path.Combine(dir.Path, "b.json"), fileTimeout: Patient, httpTimeout: Patient);
        Assert.Equal((UpdateStatus.Failed, "the response is larger than 64 KB"), (h.Status, h.Error));

        var notes = new string('n', 5000);
        var cut = await UpdateChecker.CheckAsync(Manifest(dir, "{\"version\":\"0.5.0\",\"notes\":\"" + notes + "\"}"), "0.4.0", Now, Path.Combine(dir.Path, "c.json"), force: true, fileTimeout: Patient, httpTimeout: Patient);
        Assert.Equal(UpdateChecker.MaxNotesLength, cut.Info!.Notes!.Length);
    }

    [Fact]
    public async Task Http_Manifest_UserAgent_NoIdentifiers()
    {
        using var dir = new TempDir();
        using var server = new LocalHttp(c => LocalHttp.Reply(c, 200, """{"version":"0.5.0","url":"https://example.invalid/offload.zip"}"""));

        var r = await Check(server.Url + "offload-update.json", "0.4.0-win", Now, Path.Combine(dir.Path, "s.json"));

        Assert.Equal((UpdateStatus.NewVersion, "https://example.invalid/offload.zip", null), (r.Status, r.Info?.Url, r.Error));
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
        Assert.EndsWith("did not respond within 0.3 s", r.Error);

        using var missing = new LocalHttp(c => LocalHttp.Reply(c, 404, "no"));
        var nf = await Check(missing.Url, "0.4.0", Now, Path.Combine(dir.Path, "b.json"));
        Assert.StartsWith("the server answered 404", nf.Error);
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
        Assert.EndsWith("did not respond within 0.3 s", r.Error);
    }

    // Ruling I5 + U1: what "Open" may open, decided WITHOUT touching the disk (Classify): an https URL, or a local
    // (drive letter) path; network paths are never clickable. The disk is touched only on the click (Resolve).
    [Theory]
    [InlineData("https://example.com/offload", UpdateLinkKind.Web)]
    [InlineData("http://example.com/offload", UpdateLinkKind.None)]
    [InlineData(@"\\nas\Offload\", UpdateLinkKind.Network)]
    [InlineData(@"\\nas\Offload\Offload-win-x64-0.5.0.zip", UpdateLinkKind.Network)]
    [InlineData("file://nas/Offload/", UpdateLinkKind.Network)]
    [InlineData("file://nas/Offload/setup.exe", UpdateLinkKind.Network)]
    [InlineData(@"\\.\pipe\x", UpdateLinkKind.None)]
    [InlineData("file:///C:/Builds/Offload/", UpdateLinkKind.Folder)]
    [InlineData(@"C:\Builds\Offload", UpdateLinkKind.Folder)]
    [InlineData(@"D:\Builds\Offload-win-x64-0.5.0.zip", UpdateLinkKind.File)]
    [InlineData("file:///C:/Builds/Offload-win-x64-0.5.0.ZIP", UpdateLinkKind.File)]
    [InlineData(@"C:\Builds\calc.exe"",x", UpdateLinkKind.None)]
    [InlineData(@"C:\Builds,x\Offload", UpdateLinkKind.None)]
    [InlineData("file:///C:/Builds/a%22b/", UpdateLinkKind.None)]
    [InlineData("file:///C:/Builds/a%2Cb.zip", UpdateLinkKind.None)]
    [InlineData("javascript:alert(1)", UpdateLinkKind.None)]
    [InlineData("ms-settings:network", UpdateLinkKind.None)]
    [InlineData("search-ms:query=x", UpdateLinkKind.None)]
    [InlineData("ftp://example.com/x", UpdateLinkKind.None)]
    [InlineData("offload", UpdateLinkKind.None)]
    [InlineData("", UpdateLinkKind.None)]
    [InlineData(null, UpdateLinkKind.None)]
    public void UpdateLinks_Classify_NoDisk_Table(string? url, UpdateLinkKind kind) => Assert.Equal(kind, UpdateLinks.Classify(url).Kind);

    // U1: on the click the local path is checked on the disk: a folder (shown with a trailing backslash), a zip; a
    // local exe or a missing path is refused; network and web are passed through for the caller to refuse / open.
    [Fact]
    public void UpdateLinks_Resolve_AtClick()
    {
        using var dir = new TempDir();
        var zip = Path.Combine(dir.Path, "Offload-win-x64-0.5.0.zip");
        File.WriteAllText(zip, "x");
        var exe = Path.Combine(dir.Path, "setup.exe");
        File.WriteAllText(exe, "x");

        Assert.Equal((UpdateLinkKind.Folder, dir.Path), UpdateLinks.Resolve(dir.Path));
        Assert.Equal((UpdateLinkKind.Folder, dir.Path), UpdateLinks.Resolve(new Uri(dir.Path).AbsoluteUri));
        Assert.Equal((UpdateLinkKind.File, zip), UpdateLinks.Resolve(zip));
        Assert.Equal((UpdateLinkKind.Web, "https://example.com/x"), UpdateLinks.Resolve("https://example.com/x"));
        foreach (var bad in new[] { exe, new Uri(exe).AbsoluteUri, Path.Combine(dir.Path, "gone"), Path.Combine(dir.Path, "gone.zip"),
                     "http://example.com/x", "ms-settings:network", @"\\nas-that-does-not-exist-xyz\share\x.exe" })
            Assert.NotEqual(UpdateLinkKind.Folder, UpdateLinks.Resolve(bad).Kind);
        foreach (var bad in new[] { exe, new Uri(exe).AbsoluteUri, Path.Combine(dir.Path, "gone"), Path.Combine(dir.Path, "gone.zip"), "http://example.com/x", "ms-settings:network" })
            Assert.Equal(UpdateLinkKind.None, UpdateLinks.Resolve(bad).Kind);
        Assert.Equal(UpdateLinkKind.Network, UpdateLinks.Resolve(@"\\nas-that-does-not-exist-xyz\share\x.exe").Kind);
        Assert.Equal("Network path — copy it and open it by hand.", UpdateLinks.NetworkNote);
    }

    // Hostile manifest text: a long or control-character version / URL is refused, error texts are capped, the version
    // is shown without its suffix.
    [Theory]
    [InlineData("{\"version\":\"0.5.0-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}")]
    [InlineData("{\"version\":\"0.5.0\\nX\"}")]
    [InlineData("{\"version\":\"0.5.0\\u202e\"}")]
    [InlineData("{\"version\":\"0.5.0\",\"url\":\"https://x/\\u202egpj.exe\"}")]
    [InlineData("{\"version\":\"0.5.0\",\"url\":\"https://x/\\u001b[2J\"}")]
    public async Task HostileManifest_Refused(string json)
    {
        using var dir = new TempDir();
        var r = await UpdateChecker.CheckAsync(Manifest(dir, json), "0.4.0", Now, Path.Combine(dir.Path, "s.json"));
        Assert.Equal(UpdateStatus.Failed, r.Status);
        Assert.DoesNotContain('\u001b', r.Error!);
        Assert.DoesNotContain('\n', r.Error!);
    }

    [Fact]
    public async Task HostileManifest_LongUrl_Refused_ErrorCapped_VersionShownPlain()
    {
        using var dir = new TempDir();
        var state = Path.Combine(dir.Path, "s.json");
        var longUrl = await UpdateChecker.CheckAsync(Manifest(dir, "{\"version\":\"0.5.0\",\"url\":\"https://x/" + new string('a', 1200) + "\"}"), "0.4.0", Now, state);
        Assert.Equal(UpdateStatus.Failed, longUrl.Status);

        var huge = "0.5.0-" + new string('A', 10000);
        Assert.Equal(UpdateStatus.Failed, (await UpdateChecker.CheckAsync(Manifest(dir, "{\"version\":\"" + huge + "\"}"), "0.4.0", Now, state, force: true)).Status);

        using var server = new LocalHttp(async c => { c.Response.StatusDescription = new string('x', 3000); await LocalHttp.Reply(c, 404, "no"); });
        var h = await UpdateChecker.CheckAsync(server.Url, "0.4.0", Now, state, force: true, fileTimeout: Patient, httpTimeout: Patient);
        Assert.Equal(UpdateStatus.Failed, h.Status);
        Assert.True(h.Error!.Length <= UpdateChecker.MaxErrorLength, h.Error.Length.ToString());

        var ok = await UpdateChecker.CheckAsync(Manifest(dir, "{\"version\":\"0.5.0-win\"}"), "0.4.0", Now, state, force: true, fileTimeout: Patient, httpTimeout: Patient);
        Assert.Equal("Version 0.5.0 is available (you have 0.4.0).", ok.Text("0.4.0"));
    }

    // U2: a local drive is resolved as before; DriveType.Network drives are never opened (tested on the first
    // mapped network drive, if the machine has one).
    [Fact]
    public void UpdateLinks_Resolve_NetworkDrive_IsNetwork()
    {
        var net = DriveInfo.GetDrives().FirstOrDefault(d => d.DriveType == DriveType.Network);
        if (net == null) return;   // nothing mapped here: the check is a one-liner on DriveInfo
        Assert.Equal(UpdateLinkKind.Network, UpdateLinks.Resolve(net.RootDirectory.FullName).Kind);
        Assert.Equal(UpdateLinkKind.Network, UpdateLinks.Resolve(Path.Combine(net.RootDirectory.FullName, "a.zip")).Kind);
    }

    // Notes read back from the state file are cut like those of a manifest.
    [Fact]
    public async Task StateNotes_CutTo2000()
    {
        using var dir = new TempDir();
        var state = Path.Combine(dir.Path, "update-check.json");
        var source = Manifest(dir, "{\"version\":\"0.4.0\"}");
        File.WriteAllText(state, "{\"lastCheck\":\"" + Now.ToString("o") + "\",\"lastSource\":" + JsonSerializer.Serialize(source) + ",\"lastVersion\":\"0.5.0\",\"lastNotes\":\"" + new string('n', 50000) + "\"}");
        var r = await UpdateChecker.CheckAsync(source, "0.4.0", Now.AddHours(1), state);
        Assert.Equal(UpdateStatus.NewVersion, r.Status);
        Assert.Equal(UpdateChecker.MaxNotesLength, r.Info!.Notes!.Length);
    }

    // A hostile state file is validated like a manifest.
    [Fact]
    public async Task HostileState_NotTrusted()
    {
        using var dir = new TempDir();
        var state = Path.Combine(dir.Path, "update-check.json");
        var source = Manifest(dir, "{\"version\":\"0.4.0\"}");
        File.WriteAllText(state, "{\"lastCheck\":\"" + Now.ToString("o") + "\",\"lastSource\":" + JsonSerializer.Serialize(source) + ",\"lastVersion\":\"0.9.0\\nEVIL\"}");
        var r = await UpdateChecker.CheckAsync(source, "0.4.0", Now.AddHours(1), state);
        Assert.NotEqual(UpdateStatus.NewVersion, r.Status);
    }

    // The remembered version belongs to its address: A newer, then B unreadable, then B again an hour later must not
    // report A's version.
    [Fact]
    public async Task ChangedSource_StaleVersionCleared()
    {
        using var dir = new TempDir();
        var state = Path.Combine(dir.Path, "update-check.json");
        var a = Manifest(dir, "{\"version\":\"0.5.0\"}");
        Assert.Equal(UpdateStatus.NewVersion, (await UpdateChecker.CheckAsync(a, "0.4.0", Now, state, fileTimeout: Patient, httpTimeout: Patient)).Status);
        var b = Path.Combine(dir.Path, "missing.json");
        Assert.Equal(UpdateStatus.Failed, (await UpdateChecker.CheckAsync(b, "0.4.0", Now.AddMinutes(5), state)).Status);
        var again = await UpdateChecker.CheckAsync(b, "0.4.0", Now.AddHours(1), state);
        Assert.Equal(UpdateStatus.NotDue, again.Status);
        Assert.Null(again.Info);
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
