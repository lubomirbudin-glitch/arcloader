using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Offload.Core.Desktop;

namespace Offload.Core.Updates;

/// "updates" in settings.json (spec 4.4, 5.1) — global, not part of a project.
public sealed class UpdateSettings
{
    /// Where the manifest is: an http(s) URL or a file path (local, NAS, UNC). Null = the check is off (the default).
    [JsonPropertyName("manifest")] public string? Manifest { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// The manifest `build.ps1 -ReleaseUrl` writes (spec 4.4): {"version", "url", "notes"}.
public sealed record UpdateInfo(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("notes")] string? Notes);

public enum UpdateStatus { Off, NotDue, UpToDate, NewVersion, Failed }

/// The answer of a check. `Info`: the newer version (NewVersion, also NotDue when the last check found one).
public sealed record UpdateResult(UpdateStatus Status, UpdateInfo? Info, string? Error)
{
    /// The line under "Zkontrolovat teď" (Czech), or null.
    public string? Text(string currentVersion) => Status switch
    {
        UpdateStatus.NewVersion => $"Je k dispozici verze {UpdateChecker.Plain(Info!.Version)} (máte {UpdateChecker.Plain(currentVersion)}).",
        UpdateStatus.UpToDate => $"Máte nejnovější verzi {UpdateChecker.Plain(currentVersion)}.",
        UpdateStatus.Failed => $"Manifest nejde načíst: {Error}",
        UpdateStatus.Off => "Adresa manifestu není vyplněná — kontrola je vypnutá.",
        _ => null,
    };
}

/// %APPDATA%\Offload\update-check.json: when the manifest was last read, from where, and what it said.
public sealed class UpdateState
{
    [JsonPropertyName("lastCheck")] public DateTime? LastCheck { get; set; }
    [JsonPropertyName("lastSource")] public string? LastSource { get; set; }
    [JsonPropertyName("lastVersion")] public string? LastVersion { get; set; }
    [JsonPropertyName("lastUrl")] public string? LastUrl { get; set; }
    [JsonPropertyName("lastNotes")] public string? LastNotes { get; set; }
}

/// The update check (spec 4.4, R21): reads the manifest at most once in 24 h, never downloads or starts anything;
/// HTTP with a 10 s limit, a file read on its own task with a 5 s limit (a dead UNC path would block for tens of
/// seconds); at most 64 KB is read. Never throws — a problem is UpdateStatus.Failed with a Czech reason.
public static class UpdateChecker
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan FileTimeout = TimeSpan.FromSeconds(5);
    /// A manifest is a few hundred bytes; anything larger is refused (hostile or garbage).
    public const int MaxManifestBytes = 64 * 1024;
    public const int MaxNotesLength = 2000;
    public const int MaxVersionLength = 32;
    public const int MaxUrlLength = 1000;
    /// Texts from a server (reason phrase, JSON error) are cut to this before they reach the UI or the log.
    public const int MaxErrorLength = 300;

    public static string DefaultStatePath => Path.Combine(AppData.Folder, "update-check.json");

    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// `force` ("Zkontrolovat teď") ignores the 24 h. A last check in the future (the clock went back) or one made for
    /// another address is due now.
    public static async Task<UpdateResult> CheckAsync(string? source, string currentVersion, DateTime nowUtc, string statePath,
        bool force = false, TimeSpan? httpTimeout = null, TimeSpan? fileTimeout = null)
    {
        if (string.IsNullOrWhiteSpace(source)) return new(UpdateStatus.Off, null, null);
        source = source.Trim();
        var state = LoadState(statePath);
        // What was remembered belongs to its address: another address starts from nothing.
        bool sameSource = string.Equals(state.LastSource, source, StringComparison.Ordinal);
        if (!sameSource) state.LastVersion = state.LastUrl = state.LastNotes = null;
        if (state.LastNotes is { Length: > MaxNotesLength } sn) state.LastNotes = sn[..MaxNotesLength];
        if (!force && sameSource && state.LastCheck is DateTime last && last <= nowUtc && nowUtc - last < Interval)
            return state.LastVersion is string v && SafeInfo(new UpdateInfo(v, state.LastUrl, state.LastNotes)) && IsNewer(v, currentVersion)
                ? new(UpdateStatus.NewVersion, new UpdateInfo(v, state.LastUrl, state.LastNotes), null)
                : new(UpdateStatus.NotDue, null, null);
        UpdateResult result;
        try
        {
            var text = await Read(source, httpTimeout ?? HttpTimeout, fileTimeout ?? FileTimeout).ConfigureAwait(false);
            var info = Parse(text);
            state.LastVersion = info.Version;
            state.LastUrl = info.Url;
            state.LastNotes = info.Notes;
            result = IsNewer(info.Version, currentVersion) ? new(UpdateStatus.NewVersion, info, null) : new(UpdateStatus.UpToDate, info, null);
        }
        catch (Exception e)
        {
            result = new(UpdateStatus.Failed, null, Clean(e is UpdateException ? e.Message : e.GetBaseException().Message));
        }
        state.LastCheck = nowUtc;   // also after a failure: once a day, not at every start
        state.LastSource = source;
        SaveState(statePath, state);
        return result;
    }

    /// The manifest's text: GET for http(s), otherwise a file (local, NAS, UNC). At most MaxManifestBytes.
    static async Task<string> Read(string source, TimeSpan httpTimeout, TimeSpan fileTimeout)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            using var cts = new CancellationTokenSource(httpTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd($"Offload/{Plain(OffloadVersion.String)}");
            try
            {
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw new UpdateException($"server odpověděl {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd());
                await using var body = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
                return await ReadLimited(body, "odpověď", cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new UpdateException($"{source} neodpověděl do {httpTimeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s");
            }
        }
        var read = Task.Run(async () =>
        {
            await using var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None);
            return await ReadLimited(file, "soubor", CancellationToken.None).ConfigureAwait(false);
        });
        if (await Task.WhenAny(read, Task.Delay(fileTimeout)).ConfigureAwait(false) != read)
        {
            _ = read.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);   // observed later, never unobserved
            throw new UpdateException($"{source} neodpověděl do {fileTimeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s");
        }
        try
        {
            return await read.ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new UpdateException(e.Message);
        }
    }

    /// At most MaxManifestBytes of UTF-8; one byte more is "too large" (`what`: "soubor" / "odpověď").
    static async Task<string> ReadLimited(Stream stream, string what, CancellationToken token)
    {
        var buffer = new byte[MaxManifestBytes + 1];
        int total = 0;
        while (total < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(total), token).ConfigureAwait(false);
            if (n == 0) break;
            total += n;
        }
        if (total > MaxManifestBytes) throw new UpdateException($"{what} je větší než 64 KB");
        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    /// The manifest; a version that is not MAJOR.MINOR.PATCH or a broken JSON is an UpdateException. Notes are cut.
    public static UpdateInfo Parse(string text)
    {
        UpdateInfo? info;
        try
        {
            info = JsonSerializer.Deserialize<UpdateInfo>(text.TrimStart('﻿'));
        }
        catch (JsonException e)
        {
            throw new UpdateException($"poškozený JSON ({e.Message})");
        }
        if (info?.Version is not string v) throw new UpdateException("v manifestu chybí platná verze (MAJOR.MINOR.PATCH)");
        Validate(info);
        if (info.Notes is { Length: > MaxNotesLength } n) info = info with { Notes = n[..MaxNotesLength] };
        return info;
    }

    /// Text that ends up on screen is short and has no control characters (a hostile manifest or state file).
    static void Validate(UpdateInfo info)
    {
        if (info.Version.Length > MaxVersionLength || info.Version.Any(Odd))
            throw new UpdateException("verze v manifestu je příliš dlouhá nebo obsahuje zvláštní znaky");
        if (Parts(info.Version) == null) throw new UpdateException("v manifestu chybí platná verze (MAJOR.MINOR.PATCH)");
        if (info.Url is { } u && (u.Length > MaxUrlLength || u.Any(Odd)))
            throw new UpdateException("adresa vydání v manifestu je příliš dlouhá nebo obsahuje zvláštní znaky");
    }

    /// Control and Unicode Format characters (e.g. U+202E right-to-left override) are never shown.
    static bool Odd(char c) => char.IsControl(c) || System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format;

    static bool SafeInfo(UpdateInfo info)
    {
        try { Validate(info); return true; }
        catch (UpdateException) { return false; }
    }

    /// A text from outside: control characters become spaces, the length is capped.
    public static string Clean(string text)
    {
        var s = new string([.. text.Select(c => char.IsControl(c) ? ' ' : c)]).Trim();
        return s.Length > MaxErrorLength ? s[..(MaxErrorLength - 1)] + "…" : s;
    }

    /// `candidate` > `current` by MAJOR.MINOR.PATCH; what follows "-" is ignored ("0.4.0-win" = "0.4.0").
    public static bool IsNewer(string candidate, string current) =>
        Parts(candidate) is { } a && Parts(current) is { } b && a.CompareTo(b) > 0;

    /// "0.10.0" > "0.9.0" (numbers, not text). Null when it is no version.
    public static (int, int, int)? Parts(string version)
    {
        var p = Plain(version).Split('.');
        if (p.Length is < 1 or > 3) return null;
        var n = new int[3];
        for (int i = 0; i < p.Length; i++)
            if (!int.TryParse(p[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i])) return null;
        return (n[0], n[1], n[2]);
    }

    /// The version without its suffix: "0.4.0-win" → "0.4.0".
    public static string Plain(string version) => version.Trim().Split('-', 2)[0];

    static UpdateState LoadState(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception)
        {
            return new();   // a damaged state file: check again
        }
    }

    static void SaveState(string path, UpdateState state)
    {
        try
        {
            NativeIO.Mkdirs(Path.GetDirectoryName(path)!);
            NativeIO.WriteFileReplacing(path, JsonSerializer.SerializeToUtf8Bytes(state, Json));
        }
        catch (Exception e)
        {
            System.Diagnostics.Trace.WriteLine($"update check: state not saved: {e.Message}");
        }
    }
}

/// A Czech reason the manifest could not be used.
public sealed class UpdateException(string message) : Exception(message);

public enum UpdateLinkKind { None, Web, Network, Folder, File }

/// Ruling I5 + U1: what "Otevřít" may open. Only an https URL (the browser), or a LOCAL path (a drive letter) that is an
/// existing folder or .zip (Explorer) — never anything that would run (an exe, a .lnk, a protocol handler, plain http)
/// and never a network path (UNC, file://host, anything not on a drive letter: opening it contacts SMB/WebDAV).
public static class UpdateLinks
{
    public const string NetworkNote = "Síťová cesta — zkopírujte ji a otevřete ručně.";

    static readonly System.Text.RegularExpressions.Regex DriveRoot = new(@"^[A-Za-z]:[\\/]", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// By the text alone — never touches the disk or the network (safe on the UI thread, at start and from the cache).
    /// Folder / File are candidates: a local path (.zip = File); Resolve checks them.
    public static (UpdateLinkKind Kind, string Target) Classify(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return (UpdateLinkKind.None, "");
        if (uri.Scheme == Uri.UriSchemeHttps) return (UpdateLinkKind.Web, uri.AbsoluteUri);
        if (!uri.IsFile) return (UpdateLinkKind.None, "");
        if (uri.IsUnc) return (UpdateLinkKind.Network, url.Trim());
        var path = uri.LocalPath;
        // a quote or comma could split the explorer.exe command line
        if (url.Contains('"') || url.Contains(',') || path.Contains('"') || path.Contains(',')) return (UpdateLinkKind.None, "");
        if (!DriveRoot.IsMatch(path)) return (UpdateLinkKind.Network, url.Trim());
        return (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? UpdateLinkKind.File : UpdateLinkKind.Folder, path);
    }

    static bool IsNetworkDrive(string path) => new DriveInfo(path[..1]).DriveType == DriveType.Network;

    /// At the click: Classify, then the disk is asked (call off the UI thread, with a limit): a folder must exist as a
    /// directory, a zip as a file; otherwise None. Web and Network are returned as they are.
    public static (UpdateLinkKind Kind, string Target) Resolve(string? url)
    {
        var c = Classify(url);
        try
        {
            // U2: a mapped network drive can answer "folder" to this check and "file" to Explorer
            if (c.Kind is UpdateLinkKind.Folder or UpdateLinkKind.File && IsNetworkDrive(c.Target)) return (UpdateLinkKind.Network, c.Target);
            if (c.Kind == UpdateLinkKind.Folder && !Directory.Exists(c.Target)) return (UpdateLinkKind.None, "");
            if (c.Kind == UpdateLinkKind.File && !File.Exists(c.Target)) return (UpdateLinkKind.None, "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (UpdateLinkKind.None, "");
        }
        return c;
    }
}
