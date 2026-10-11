using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Offload.Core.Updates;

namespace Offload.Core.Notify;

/// What a send did: Ok, or the Czech reason (for the test button and the log).
public sealed record NtfyResult(bool Ok, string? Error);

/// A push through an ntfy server (spec 4.3, R20): one JSON POST to `<server>/` — HTTP headers cannot carry Czech —
/// one attempt, 10 s at most, never an exception. Without a token (see the spec: no plain-text secrets).
public static partial class NtfyClient
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    // One client for the process; the time limit is per request (a CancellationToken), not the client's.
    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    // Czech as UTF-8 text in the JSON (not \u escapes).
    static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}\z")]
    private static partial Regex TopicPattern();

    /// Why `topic` cannot be used (Czech), or null. ntfy accepts letters without diacritics, digits, "-" and "_".
    public static string? TopicProblem(string? topic) =>
        string.IsNullOrWhiteSpace(topic) ? "The topic is empty."
        : TopicPattern().IsMatch(topic.Trim()) ? null
        : "The topic may contain only letters without accents, digits, - and _ (at most 64 characters).";

    /// Why `server` cannot be used (Czech), or null: an http(s) address.
    public static string? ServerProblem(string? server) =>
        Uri.TryCreate(server?.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) && u.Host.Length > 0
            ? null
            : $"The server address \"{server}\" is not an http(s) address.";

    /// Why the ntfy settings cannot be kept (the dialog's OK), or null: switched on, they need a server and a topic.
    public static string? SettingsProblem(NtfySettings s) => s.Enabled ? ServerProblem(s.Server) ?? TopicProblem(s.Topic) : null;

    /// CLI --ntfy <url>: "https://ntfy.sh/muj-tajny-kanal" → ("https://ntfy.sh", "muj-tajny-kanal"); the last path segment
    /// is the topic. Null when the URL has no topic or is no http(s) address.
    public static (string Server, string Topic)? ParseUrl(string url)
    {
        var trimmed = url.Trim().TrimEnd('/');
        int slash = trimmed.LastIndexOf('/');
        if (slash < 0) return null;
        var (server, topic) = (trimmed[..slash], trimmed[(slash + 1)..]);
        return ServerProblem(server) == null && TopicProblem(topic) == null && !server.EndsWith(':') ? (server, topic) : null;
    }

    /// Sends one notification. `timeout` (tests): instead of 10 s.
    public static async Task<NtfyResult> SendAsync(string server, string topic, string title, string message, int priority,
        IReadOnlyList<string> tags, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (ServerProblem(server) is string badServer) return new(false, badServer);
        if (TopicProblem(topic) is string badTopic) return new(false, badTopic);
        var limit = timeout ?? DefaultTimeout;
        var body = new JsonObject
        {
            ["topic"] = topic.Trim(),
            ["title"] = title,
            ["message"] = message,
            ["priority"] = priority,
            ["tags"] = new JsonArray([.. tags.Select(t => (JsonNode?)JsonValue.Create(t))]),
        };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(limit);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, server.Trim().TrimEnd('/') + "/")
            {
                Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(body.ToJsonString(Json))),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            request.Headers.UserAgent.ParseAdd($"{Product.Name}/{UpdateChecker.Plain(OffloadVersion.String)}");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new(true, null)
                : new(false, $"The server answered {(int)response.StatusCode} {UpdateChecker.Clean(response.ReasonPhrase ?? "")}".TrimEnd() + ".");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, $"The server did not answer within {limit.TotalSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} s.");
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or InvalidOperationException or UriFormatException)
        {
            return new(false, e.GetBaseException().Message);
        }
    }

    /// The phone notification of a finished job, when ntfy is on in the job's settings (spec 4.3). Never throws, never
    /// waited for by a job: the result goes to `log` only.
    public static async Task SendOutcomeAsync(NtfySettings settings, JobOutcome? outcome, Action<string>? log, TimeSpan? timeout = null)
    {
        if (!settings.Enabled || outcome is not { ForPhone: true }) return;
        try
        {
            var r = await SendAsync(settings.Server, settings.Topic, outcome.Title, outcome.PhoneMessage, outcome.Priority, outcome.Tags, timeout).ConfigureAwait(false);
            log?.Invoke(r.Ok ? "ntfy: sent" : $"ntfy: {r.Error}");
        }
        catch (Exception e)
        {
            log?.Invoke($"ntfy: {e.Message}");
        }
    }

    /// "Send test notification".
    public static Task<NtfyResult> SendTestAsync(NtfySettings settings, TimeSpan? timeout = null) =>
        SendAsync(settings.Server, settings.Topic, $"{Product.Name}: test notification", $"Notifications from {Product.Name} work.", 3, ["white_check_mark"], timeout);
}
