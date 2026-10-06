using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Offload.Core.Desktop;

namespace Offload.Core.History;

/// %APPDATA%\Offload\history.json + history\<id>.files.gz (spec 2.1, 5.2). Local to this PC, never sent anywhere.
/// The GUI and the CLI may run at once: every read-modify-write holds the named mutex `Local\Offload-history`
/// (at most 5 s, then the entry is skipped with a log line), and the file is replaced via a temporary name.
/// Never throws — a broken history must never stop an offload.
public sealed partial class HistoryStore(string folder, Action<string>? log = null)
{
    public const int CurrentVersion = 1;
    const string MutexName = @"Local\Offload-history";

    /// %APPDATA%\Offload (or OFFLOAD_SETTINGS_DIR). Tests set DefaultFolderOverride once, so no test ever writes into
    /// the developer's own history — not even while another test changes OFFLOAD_SETTINGS_DIR.
    public static HistoryStore Default(Action<string>? log = null) => new(DefaultFolderOverride ?? AppData.Folder, log);

    internal static string? DefaultFolderOverride { get; set; }

    public string Folder { get; } = folder;
    public string FilePath => Path.Combine(Folder, "history.json");
    /// Spec 5.2: at most 5 000 entries; the oldest by `finished` go, with their file lists.
    internal int MaxEntries { get; init; } = 5000;
    internal TimeSpan LockTimeout { get; init; } = TimeSpan.FromSeconds(5);

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new OffloadReport.IsoSecondsDateConverter() },
    };

    sealed class HistoryFile
    {
        [JsonPropertyName("version")] public int Version { get; set; } = CurrentVersion;
        [JsonPropertyName("entries")] public List<HistoryEntry> Entries { get; set; } = [];
    }

    [GeneratedRegex(@"^[0-9A-Za-z-]{1,64}\z")]
    private static partial Regex SafeId();

    /// "20261006T081500Z-3f2a9c1b": the finish time in UTC and 8 random hex digits.
    public static string NewId(DateTime finishedUtc) =>
        finishedUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-"
        + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));

    /// Every entry, newest `finished` first. Missing → empty; damaged → set aside as history.json.corrupt-<stamp>
    /// (only while holding the lock), logged, empty.
    public IReadOnlyList<HistoryEntry> Load()
    {
        try
        {
            using var held = Lock();
            return ReadFile(repair: held != null).Entries.OrderByDescending(e => e.Finished).ToList();
        }
        catch (Exception e)
        {
            log?.Invoke($"history: {e.Message}");
            return [];
        }
    }

    /// Adds `entry`, or replaces the entry with the same id (an append), writes its file list and prunes. False when
    /// nothing was written: the lock stayed busy, the file is from a newer version, or the disk failed — logged.
    public bool Record(HistoryEntry entry, IReadOnlyList<SourceFile>? files) => Update(entry.Id, _ => entry, files);

    /// Load, modify and save under ONE hold of the lock (ruling I2): `modify` gets the current entry with this id (null
    /// when there is none) and returns the entry to store, or null to change nothing. It must keep the id. `files`, when
    /// given, replaces the entry's file list. False when nothing was written (lock busy, newer version, null result,
    /// disk failure) — logged. Keep `modify` quick: the lock is held meanwhile.
    public bool Update(string id, Func<HistoryEntry?, HistoryEntry?> modify, IReadOnlyList<SourceFile>? files = null)
    {
        if (!SafeId().IsMatch(id))
        {
            log?.Invoke($"history: invalid id \"{id}\" — not written");
            return false;
        }
        try
        {
            using var held = Lock();
            if (held == null)
            {
                log?.Invoke($"history: busy for {LockTimeout.TotalSeconds:0} s — the entry {id} is skipped");
                return false;
            }
            var (entries, version) = ReadFile(repair: true);
            if (version > CurrentVersion)
            {
                log?.Invoke($"history: version {version} is newer than this app — read only, the entry {id} is skipped");
                return false;
            }
            var entry = modify(entries.FirstOrDefault(e => e.Id == id));
            if (entry == null) return false;
            if (entry.Id != id)
            {
                log?.Invoke($"history: the entry changed its id from {id} to \"{entry.Id}\" — not written");
                return false;
            }
            if (files != null)
            {
                FileListStore.Write(FileListPath(id)!, files);
                entry.FileList = $"history/{id}.files.gz";
            }
            entries.RemoveAll(e => e.Id == id);
            entries.Add(entry);
            var keep = entries.OrderByDescending(e => e.Finished).Take(MaxEntries).ToList();
            foreach (var gone in entries.Except(keep))
                if (FileListPath(gone.Id) is string list) NativeIO.Delete(list);
            NativeIO.Mkdirs(Folder);
            NativeIO.WriteFileReplacing(FilePath, JsonSerializer.SerializeToUtf8Bytes(new HistoryFile { Entries = keep }, Json));
            return true;
        }
        catch (Exception e)
        {
            log?.Invoke($"history: {e.Message}");
            return false;
        }
    }

    /// The entry's file list, or null when it is missing or damaged.
    public IReadOnlyList<SourceFile>? ReadFileList(HistoryEntry entry) =>
        FileListPath(entry.Id) is string path ? FileListStore.Read(path) : null;

    /// The newest entry this card may be (spec 2.1, steps 1–2): for a volume root the same file-system serial and
    /// capacity (R1), for a folder the same shape. Null when there is none.
    public static HistoryEntry? FindForCard(IEnumerable<HistoryEntry> entries, CardKey? key, string shape) =>
        entries.Where(e => key != null
                ? e.Card is { } c && string.Equals(c.FsSerial, key.FsSerial, StringComparison.OrdinalIgnoreCase) && c.Capacity == key.Capacity
                : e.Shape == shape)
            .OrderByDescending(e => e.Finished)
            .FirstOrDefault();

    /// Derived from the id (never from the stored "fileList"), so a hand-edited file cannot point anywhere else.
    string? FileListPath(string id) => SafeId().IsMatch(id) ? Path.Combine(Folder, "history", id + ".files.gz") : null;

    (List<HistoryEntry> Entries, int Version) ReadFile(bool repair)
    {
        if (!File.Exists(FilePath)) return ([], CurrentVersion);
        byte[] bytes;
        // Shared with writers: an open reader must never block the writer's rename (it would lose the entry).
        using (var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bytes = new byte[fs.Length];
            fs.ReadExactly(bytes);
        }
        int version;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            version = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("version", out var v)
                      && v.TryGetInt32(out var n) ? n : throw new JsonException("no version");
        }
        catch (JsonException e)
        {
            if (repair) SetAside(e.Message);
            return ([], CurrentVersion);
        }
        try
        {
            var file = JsonSerializer.Deserialize<HistoryFile>(bytes, Json);
            var entries = file?.Entries ?? [];
            // A hand-edited null entry or id is damage (set aside below), not something to crash on (minors M5).
            if (entries.Any(e => e == null || string.IsNullOrEmpty(e.Id))) throw new JsonException("entry without id");
            foreach (var e in entries) Normalise(e);
            return (entries, version);
        }
        catch (Exception e) when (e is JsonException or FormatException or ArgumentException or InvalidOperationException)
        {
            // A newer version may have changed the entries: never set its file aside, just do not use it.
            if (version > CurrentVersion) return ([], version);
            if (repair) SetAside(e.Message);
            return ([], CurrentVersion);
        }
    }

    /// Hand-edited nulls become empty values, so nothing downstream needs a null check.
    static void Normalise(HistoryEntry e)
    {
        e.Source ??= "";
        e.CardName ??= "";
        e.Date ??= "";
        e.Fingerprint ??= "";
        e.Shape ??= "";
        e.HashAlgorithms = (e.HashAlgorithms ?? []).Where(h => h != null).ToList();
        e.Reels = (e.Reels ?? []).Where(r => r != null).ToList();
        e.Destinations = (e.Destinations ?? []).Where(d => d != null).ToList();
        foreach (var d in e.Destinations) { d.Root ??= ""; d.CardPath ??= ""; }
        e.Generations = (e.Generations ?? []).Where(g => g != null).ToList();
        foreach (var g in e.Generations) g.Kind ??= HistoryGeneration.Full;
        if (e.Card != null) e.Card.FsSerial ??= "";
    }

    void SetAside(string why)
    {
        var stamp = FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var target = stamp;
        for (int n = 2; File.Exists(target); n++) target = $"{stamp}-{n}";   // never overwrite an earlier set-aside file
        try
        {
            File.Move(FilePath, target, overwrite: false);
            log?.Invoke($"history.json is damaged ({why}) — set aside as {Path.GetFileName(target)}, starting empty");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"history.json is damaged ({why}) and could not be set aside: {e.Message}");
        }
    }

    sealed class Held(Mutex mutex) : IDisposable
    {
        public void Dispose()
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }

    /// The cross-process lock, or null when another writer held it for LockTimeout. Released on the same thread.
    Held? Lock()
    {
        var deadline = DateTime.UtcNow + LockTimeout;
        while (true)
        {
            Mutex? mutex = null;
            try
            {
                mutex = new Mutex(false, MutexName);
                var left = deadline - DateTime.UtcNow;
                if (mutex.WaitOne(left > TimeSpan.Zero ? left : TimeSpan.Zero)) return new Held(mutex);
                mutex.Dispose();
                return null;
            }
            catch (AbandonedMutexException)
            {
                // A process died holding it: we own it now, and the file was replaced atomically, so it is consistent.
                return new Held(mutex!);
            }
            catch (UnauthorizedAccessException)
            {
                // The mutex was created by an elevated process: we cannot open it — the same as busy; retry until the timeout.
                mutex?.Dispose();
                if (DateTime.UtcNow >= deadline) return null;
                Thread.Sleep(50);
            }
        }
    }
}
