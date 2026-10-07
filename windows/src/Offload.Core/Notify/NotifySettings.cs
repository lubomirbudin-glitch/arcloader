using System.Text.Json;
using System.Text.Json.Serialization;

namespace Offload.Core.Notify;

/// "notify" in settings.json (spec 4.3, 5.1) — part of a project: where a job's end is announced.
public sealed class NotifySettings
{
    /// A Windows notification (tray balloon) when the main window is not in front — on by default.
    [JsonPropertyName("windows")] public bool Windows { get; set; } = true;
    [JsonPropertyName("ntfy")] public NtfySettings Ntfy { get; set; } = new();

    /// Keys of this section a newer version wrote: kept and written back (R24).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// "notify.ntfy": a push to a phone through an ntfy server (R20) — off by default, no token.
public sealed class NtfySettings
{
    public const string DefaultServer = "https://ntfy.sh";

    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("server")] public string Server { get; set; } = DefaultServer;
    [JsonPropertyName("topic")] public string Topic { get; set; } = "";

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
