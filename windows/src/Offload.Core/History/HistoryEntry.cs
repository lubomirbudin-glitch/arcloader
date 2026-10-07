using System.Text.Json.Serialization;

namespace Offload.Core.History;

/// The card volume of an entry (spec 5.2); null for a manually added folder.
public sealed class HistoryCard
{
    /// `CardIdentity` at the time ("\\?\Volume{…}\|1A2B3C4D") — stored, never required to match (R1).
    [JsonPropertyName("identity")] public string? Identity { get; set; }
    [JsonPropertyName("fsSerial")] public string FsSerial { get; set; } = "";
    [JsonPropertyName("capacity")] public long Capacity { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("filesystem")] public string? Filesystem { get; set; }
}

public sealed class HistoryDestination
{
    [JsonPropertyName("root")] public string Root { get; set; } = "";
    [JsonPropertyName("cardPath")] public string CardPath { get; set; } = "";
}

/// One copy into the card folder: the full copy (seq 1) or an append (seq = its ASC MHL generation).
public sealed class HistoryGeneration
{
    public const string Full = "full";
    public const string Append = "append";
    /// "Zkopírovat znovu": the files that failed, copied again where they failed (Windows from 0.5.0).
    public const string Repair = "repair";

    [JsonPropertyName("seq")] public int Seq { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = Full;
    [JsonPropertyName("finished")] public DateTime Finished { get; set; }
    [JsonPropertyName("files")] public int Files { get; set; }
    [JsonPropertyName("bytes")] public long Bytes { get; set; }
    [JsonPropertyName("verified")] public bool Verified { get; set; }
}

/// One card offload in %APPDATA%\Offload\history.json (spec 5.2). An append updates its entry instead of adding one.
public sealed class HistoryEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("card")] public HistoryCard? Card { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("cardName")] public string CardName { get; set; } = "";
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    /// Filled from plan 8 (projects); null until then.
    [JsonPropertyName("project")] public string? Project { get; set; }
    [JsonPropertyName("finished")] public DateTime Finished { get; set; }
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("verified")] public bool Verified { get; set; }
    /// Only when `success` is false.
    [JsonPropertyName("failedFiles"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? FailedFiles { get; set; }
    [JsonPropertyName("hashAlgorithms")] public List<string> HashAlgorithms { get; set; } = ["xxh64"];
    [JsonPropertyName("files")] public int Files { get; set; }
    [JsonPropertyName("bytes")] public long Bytes { get; set; }
    [JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = "";
    [JsonPropertyName("shape")] public string Shape { get; set; } = "";
    /// "history/<id>.files.gz" — informational; the store derives the path from `id`.
    [JsonPropertyName("fileList")] public string? FileList { get; set; }
    [JsonPropertyName("destinations")] public List<HistoryDestination> Destinations { get; set; } = [];
    [JsonPropertyName("generations")] public List<HistoryGeneration> Generations { get; set; } = [];
    [JsonPropertyName("reels")] public List<string> Reels { get; set; } = [];
}

/// How a card volume is recognised again (R1): the file system's serial number plus the capacity. The volume GUID is
/// not part of it — Windows gives the same card another GUID in another reader or slot.
public sealed record CardKey(string FsSerial, long Capacity);
