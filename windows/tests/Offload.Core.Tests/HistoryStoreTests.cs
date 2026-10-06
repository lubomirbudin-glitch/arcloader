using System.Text.Json;
using Offload.Core.History;

namespace Offload.Core.Tests;

public static class HistoryFixtures
{
    public static readonly DateTime T0 = new(2026, 10, 6, 8, 15, 0, DateTimeKind.Utc);

    /// A finished full copy of a card volume (serial 1A2B3C4D, 256 GB) onto D:\Zaloha and F:\Zaloha.
    public static HistoryEntry Entry(DateTime finished, string cardName = "A001", string? serial = "1A2B3C4D", long capacity = 256_060_514_304,
        string fingerprint = "f", string shape = "s", bool success = true, bool verified = true)
    {
        var date = finished.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return new HistoryEntry
        {
            Id = HistoryStore.NewId(finished),
            Card = serial == null ? null : new HistoryCard { Identity = @"\\?\Volume{1}\|" + serial, FsSerial = serial, Capacity = capacity, Label = cardName, Filesystem = "exFAT" },
            Source = @"E:\",
            CardName = cardName,
            Date = date,
            Finished = finished,
            Success = success,
            Verified = verified,
            FailedFiles = success ? null : 3,
            Files = 2,
            Bytes = 3000,
            Fingerprint = fingerprint,
            Shape = shape,
            Destinations =
            [
                new HistoryDestination { Root = @"D:\Zaloha", CardPath = $@"D:\Zaloha\{date}\{cardName}" },
                new HistoryDestination { Root = @"F:\Zaloha", CardPath = $@"F:\Zaloha\{date}\{cardName}" },
            ],
            Generations = [new HistoryGeneration { Seq = 1, Kind = HistoryGeneration.Full, Finished = finished, Files = 2, Bytes = 3000, Verified = verified }],
            Reels = [cardName],
        };
    }
}

public class HistoryStoreTests
{
    static readonly DateTime T0 = HistoryFixtures.T0;

    [Fact]
    public void RoundTrip_FormatOfSpec52()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);
        var entry = HistoryFixtures.Entry(T0);
        SourceFile[] files = [new("CLIP/a.mov", 1000, T0), new("CLIP/b.mov", 2000, T0)];

        Assert.True(store.Record(entry, files));

        var back = Assert.Single(store.Load());
        Assert.Equal(entry.Id, back.Id);
        Assert.Equal("1A2B3C4D", back.Card!.FsSerial);
        Assert.Equal(256_060_514_304, back.Card.Capacity);
        Assert.Equal(T0, back.Finished);
        Assert.Equal(DateTimeKind.Utc, back.Finished.Kind);
        Assert.Equal(["xxh64"], back.HashAlgorithms);
        Assert.Equal(@"F:\Zaloha", back.Destinations[1].Root);
        Assert.Equal($"history/{entry.Id}.files.gz", back.FileList);
        Assert.Equal(files, store.ReadFileList(back));

        var json = File.ReadAllText(store.FilePath);
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"finished\": \"2026-10-06T08:15:00Z\"", json);
        Assert.Contains("\"project\": null", json);
        Assert.DoesNotContain("failedFiles", json);   // only when success is false
        Assert.DoesNotContain("\r", json);
        Assert.True(File.Exists(Path.Combine(dir.Path, "history", entry.Id + ".files.gz")));
    }

    [Fact]
    public void Missing_IsEmpty()
    {
        using var dir = new TempDir();
        Assert.Empty(new HistoryStore(Path.Combine(dir.Path, "nope")).Load());
    }

    // Review focus 4: a damaged history.json is set aside, never blocks.
    [Theory]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("{\"entries\":[]}")]
    [InlineData("{\"version\":1,\"entries\":[{\"id\":\"x\",\"finished\":\"yesterday\"}]}")]
    [InlineData("{\"version\":1,\"entries\":[{\"id\":null,\"finished\":\"2026-10-06T08:15:00Z\"}]}")]   // hand-edited null id
    [InlineData("{\"version\":1,\"entries\":[null]}")]
    [InlineData("{\"version\":1,\"entries\":[{\"id\":\"x\",\"finished\":null}]}")]
    public void Damaged_SetAside_StartsEmpty_NextRecordWorks(string content)
    {
        using var dir = new TempDir();
        var log = new List<string>();
        var store = new HistoryStore(dir.Path, log.Add);
        File.WriteAllText(store.FilePath, content);

        Assert.Empty(store.Load());

        Assert.False(File.Exists(store.FilePath));
        var aside = Assert.Single(Directory.GetFiles(dir.Path, "history.json.corrupt-*"));
        Assert.Equal(content, File.ReadAllText(aside));
        Assert.Contains(log, l => l.Contains("damaged"));
        Assert.True(store.Record(HistoryFixtures.Entry(T0), null));
        Assert.Single(store.Load());
    }

    // Review focus 4: GUI and CLI writing at once — the mutex keeps every entry.
    [Fact]
    public void TwoWriters_InTwoThreads_KeepEveryEntry()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);
        void Write(int offset)
        {
            for (int i = 0; i < 25; i++) Assert.True(store.Record(HistoryFixtures.Entry(T0.AddMinutes(offset + i)), null));
        }
        var a = new Thread(() => Write(0));
        var b = new Thread(() => Write(100));
        a.Start();
        b.Start();
        a.Join();
        b.Join();
        Assert.Equal(50, store.Load().Count);
    }

    [Fact]
    public void Busy_SkipsTheEntry_AfterTheTimeout()
    {
        using var dir = new TempDir();
        var log = new List<string>();
        var store = new HistoryStore(dir.Path, log.Add) { LockTimeout = TimeSpan.FromMilliseconds(200) };
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using var m = new Mutex(false, @"Local\Offload-history");
            m.WaitOne();
            held.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            m.ReleaseMutex();
        });
        holder.Start();
        Assert.True(held.Wait(TimeSpan.FromSeconds(30)));
        try
        {
            Assert.False(store.Record(HistoryFixtures.Entry(T0), null));
        }
        finally
        {
            release.Set();
            holder.Join();
        }
        Assert.Contains(log, l => l.Contains("busy"));
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void Prune_KeepsTheNewest_AndDeletesOldFileLists()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path) { MaxEntries = 3 };
        var entries = Enumerable.Range(0, 5).Select(i => HistoryFixtures.Entry(T0.AddDays(i))).ToList();
        foreach (var e in entries.AsEnumerable().Reverse()) store.Record(e, [new SourceFile("a.mov", 1, T0)]);

        Assert.Equal(entries.Skip(2).Reverse().Select(e => e.Id), store.Load().Select(e => e.Id));
        Assert.False(File.Exists(Path.Combine(dir.Path, "history", entries[0].Id + ".files.gz")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "history", entries[1].Id + ".files.gz")));
        Assert.True(File.Exists(Path.Combine(dir.Path, "history", entries[4].Id + ".files.gz")));
    }

    [Fact]
    public void SameId_ReplacesTheEntry()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);
        var entry = HistoryFixtures.Entry(T0);
        store.Record(entry, null);
        entry.Files = 14;
        entry.Generations.Add(new HistoryGeneration { Seq = 2, Kind = HistoryGeneration.Append, Finished = T0.AddHours(1), Files = 12 });
        store.Record(entry, null);
        var back = Assert.Single(store.Load());
        Assert.Equal(14, back.Files);
        Assert.Equal([1, 2], back.Generations.Select(g => g.Seq));
    }

    // Minors M5: a hand-edited null collection or text must not make Load/Record throw and switch history off.
    [Fact]
    public void NullFields_AreNormalised_AndRecordStillWorks()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);
        File.WriteAllText(store.FilePath, "{\"version\":1,\"entries\":[{\"id\":\"a\",\"finished\":\"2026-10-06T08:15:00Z\","
            + "\"cardName\":null,\"source\":null,\"destinations\":null,\"generations\":[null],\"reels\":null,\"hashAlgorithms\":null,\"card\":{\"fsSerial\":null}}]}");

        var loaded = Assert.Single(store.Load());
        Assert.Equal("", loaded.CardName);
        Assert.Empty(loaded.Destinations);
        Assert.Empty(loaded.Generations);
        Assert.Empty(loaded.Reels);
        Assert.Empty(loaded.HashAlgorithms);
        Assert.Equal("", loaded.Card!.FsSerial);
        Assert.True(store.Record(HistoryFixtures.Entry(T0), null));
        Assert.Equal(2, store.Load().Count);
    }

    // Ruling I2: load-modify-save under ONE mutex hold — two threads appending to the same entry lose nothing.
    [Fact]
    public void Update_TwoThreadsOnTheSameEntry_LoseNoGeneration()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);
        var entry = HistoryFixtures.Entry(T0);
        Assert.True(store.Record(entry, null));
        void Append(int baseSeq)
        {
            for (int i = 0; i < 20; i++)
                Assert.True(store.Update(entry.Id, e =>
                {
                    e!.Generations.Add(new HistoryGeneration { Seq = baseSeq + i, Kind = HistoryGeneration.Append, Finished = T0 });
                    e.Files++;
                    return e;
                }));
        }
        var a = new Thread(() => Append(100));
        var b = new Thread(() => Append(200));
        a.Start();
        b.Start();
        a.Join();
        b.Join();
        var back = Assert.Single(store.Load());
        Assert.Equal(41, back.Generations.Count);
        Assert.Equal(2 + 40, back.Files);
    }

    [Fact]
    public void Update_Missing_PassesNull_AndNullResultWritesNothing()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);
        bool sawNull = false;
        Assert.False(store.Update("nope", e => { sawNull = e == null; return null; }));
        Assert.True(sawNull);
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void Update_CanAddWithFileList_AndRejectsAChangedId()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);
        var entry = HistoryFixtures.Entry(T0);
        Assert.True(store.Update(entry.Id, _ => entry, [new SourceFile("a.mov", 1, T0)]));
        Assert.NotNull(store.ReadFileList(Assert.Single(store.Load())));
        Assert.False(store.Update(entry.Id, e => { e!.Id = "other"; return e; }));
    }

    [Fact]
    public void NewerVersion_IsReadOnly()
    {
        using var dir = new TempDir();
        var log = new List<string>();
        var store = new HistoryStore(dir.Path, log.Add);
        var newer = JsonSerializer.Serialize(new { version = 2, entries = new[] { new { id = "a", cardName = "A001", finished = "2026-10-06T08:15:00Z" } } });
        File.WriteAllText(store.FilePath, newer);

        Assert.Equal("A001", Assert.Single(store.Load()).CardName);
        Assert.False(store.Record(HistoryFixtures.Entry(T0), null));
        Assert.Equal(newer, File.ReadAllText(store.FilePath));
        Assert.Contains(log, l => l.Contains("newer"));
    }

    [Fact]
    public void InvalidId_IsNeverWritten()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);
        var entry = HistoryFixtures.Entry(T0);
        entry.Id = @"..\..\evil";
        Assert.False(store.Record(entry, [new SourceFile("a", 1, T0)]));
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "history")));
    }

    // R1: serial + capacity, not the volume GUID; a clone with the same serial but another capacity is another card.
    [Fact]
    public void FindForCard_BySerialAndCapacity_OrByShape_NewestWins()
    {
        var old = HistoryFixtures.Entry(T0);
        var newer = HistoryFixtures.Entry(T0.AddHours(2));
        var clone = HistoryFixtures.Entry(T0.AddHours(3), capacity: 128_000_000_000);
        var folder = HistoryFixtures.Entry(T0.AddHours(4), serial: null, shape: "folder-shape");
        HistoryEntry[] all = [old, newer, clone, folder];

        Assert.Same(newer, HistoryStore.FindForCard(all, new CardKey("1a2b3c4d", 256_060_514_304), "x"));
        Assert.Same(clone, HistoryStore.FindForCard(all, new CardKey("1A2B3C4D", 128_000_000_000), "x"));
        Assert.Null(HistoryStore.FindForCard(all, new CardKey("99999999", 256_060_514_304), "folder-shape"));
        Assert.Same(folder, HistoryStore.FindForCard(all, null, "folder-shape"));
        Assert.Null(HistoryStore.FindForCard(all, null, "other"));
    }

    [Fact]
    public void FiveThousandEntries_LoadQuickly()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);
        var many = Enumerable.Range(0, 5000).Select(i => HistoryFixtures.Entry(T0.AddMinutes(i))).ToList();
        // written in one go through the private format: Record 5 000 times would take minutes
        File.WriteAllText(store.FilePath, JsonSerializer.Serialize(new { version = 1, entries = many }));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(5000, store.Load().Count);
        Assert.True(sw.ElapsedMilliseconds < 3000, $"{sw.ElapsedMilliseconds} ms");   // spec 5.5: 300 ms; generous under a loaded suite
    }
}
