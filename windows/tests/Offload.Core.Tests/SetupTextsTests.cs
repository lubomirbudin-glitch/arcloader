using Offload.Core.Desktop;
using Offload.Core.History;

namespace Offload.Core.Tests;

/// The left column (UI návrh obr. 1–3) and the right column's today (nová věc 7).
public class SetupTextsTests
{
    static readonly LutCatalog Cat = LutCatalog.Load(LutCatalog.DefaultFolder);

    [Fact]
    public void AfterCopy_FourRows_AsTheDesign()
    {
        var p = new SettingsProfile { Proxy = new ProxySettings { Enabled = true } };
        var rows = SetupTexts.AfterCopy(p, Cat, new CameraLabel("ALEXA 35", "LogC4"));
        Assert.Equal([("Ověření zpětným čtením", "xxHash64", true), ("Analýza klipů a náhledy", "3 snímky / klip", true),
                      ("Proxy", "1080p · H.264 · LogC4 → 709", true), ("Report", "PDF · HTML · ASC MHL · CSV", true)],   // CSV is on by default
            rows.Select(r => (r.Label, r.Value, r.On)));
        Assert.All(rows, r => Assert.False(r.Warn));

        var off = new SettingsProfile { Verify = false, AnalyzeClips = false, Hash = "both", Outputs = new Outputs.OutputSettings { ClipListCsv = false, ClipListAle = true } };
        rows = SetupTexts.AfterCopy(off, Cat, null);
        Assert.Equal(("vypnuto", false, true), (rows[0].Value, rows[0].On, rows[0].Warn));
        Assert.Equal(("vypnuto", false), (rows[1].Value, rows[1].On));
        Assert.Equal(("vypnuto", false), (rows[2].Value, rows[2].On));
        Assert.Equal("PDF · HTML · ASC MHL · ALE", rows[3].Value);
    }

    [Fact]
    public void LutShort_EveryMode()
    {
        Assert.Equal("LUT podle kamery", SetupTexts.LutShort(new ProxySettings(), Cat, null));
        Assert.Equal("S-Log3 → 709", SetupTexts.LutShort(new ProxySettings(), Cat, new CameraLabel("FX6", "S-Log3")));
        Assert.Equal("LogC3 → 709", SetupTexts.LutShort(new ProxySettings { LutMode = LutMode.Builtin, LutBuiltin = "arri-logc3-awg3" }, Cat, null));
        Assert.Equal("bez LUTu", SetupTexts.LutShort(new ProxySettings { LutMode = LutMode.None }, Cat, null));
        Assert.Equal("vlastní LUT", SetupTexts.LutShort(new ProxySettings { LutMode = LutMode.Custom, LutPath = @"C:\a.cube" }, Cat, null));
        Assert.Equal(["H.264", "H.265", "ProRes Proxy", "ProRes LT", "ProRes 422", "ProRes HQ"], Enum.GetValues<ProxyCodec>().Select(SetupTexts.CodecShort));
    }

    [Fact]
    public void Compact_Line_StartBar_Empty()
    {
        var p = new SettingsProfile { Proxy = new ProxySettings { Enabled = true } };
        Assert.Equal("Ověření xxHash64 · analýza a náhledy · proxy 1080p H.264 · report PDF, HTML, MHL", SetupTexts.AfterCopyLine(p));
        Assert.Equal("Bez ověření · report PDF, HTML, MHL", SetupTexts.AfterCopyLine(new SettingsProfile { Verify = false, AnalyzeClips = false }));
        Assert.Equal(("1 karta → 2 disky", " · 1,70 GB"), SetupTexts.StartSummary(1, 2, 1_700_000_000));
        Assert.Equal(("3 karty → 1 disk", (string?)null), SetupTexts.StartSummary(3, 1, null));
        Assert.Equal("Vyberte kartu", SetupTexts.StartSummary(0, 2, 5).Counts);
        Assert.Equal(("Vložte kartu do čtečky", "Objeví se tady automaticky."), SetupTexts.NoCard(false));
        Assert.Equal("Vložte další kartu", SetupTexts.NoCard(true).Title);
        Assert.True(SetupTexts.Compact(hasSources: false, hasJobs: true));
        Assert.False(SetupTexts.Compact(hasSources: true, hasJobs: true));
        Assert.False(SetupTexts.Compact(hasSources: false, hasJobs: false));
        Assert.Equal(@"…\2026-10-07\A035_ARRI\", SetupTexts.PathPreview("2026-10-07", "A035_ARRI"));
        Assert.Equal(@"…\2026-10-07\<název karty>\", SetupTexts.PathPreview("2026-10-07", null));
    }

    [Fact]
    public void QueueEmpty_AndFlow()
    {
        Assert.Equal(("Připraveno — A035_ARRI na 2 disky",
                "Karta se přečte jednou, zapíše na oba disky zároveň a každý soubor se zpětně ověří. Další kartu můžete vložit kdykoli — zařadí se do fronty."),
            SetupTexts.QueueEmpty(["A035_ARRI"], 2, verify: true));
        Assert.Equal("Připraveno — 3 karty na 1 disk", SetupTexts.QueueEmpty(["A", "B", "C"], 1, true).Title);
        Assert.Contains("zapíše na všechny 3 disky zároveň; zpětné ověření je vypnuté.", SetupTexts.QueueEmpty(["A"], 3, verify: false).Text);
        Assert.Equal("Zatím žádný offload", SetupTexts.QueueEmpty([], 2, true).Title);
        Assert.Equal(["Karta", "2 disky naráz", "Ověření", "Proxy", "Report"], SetupTexts.Flow(2, true, true, true));
        Assert.Equal(["Karta", "1 disk", "Analýza", "Report"], SetupTexts.Flow(1, false, true, false));
    }

    static HistoryEntry Entry(string name, DateTime finishedLocal, long bytes, bool success = true, bool verified = true, int? failed = null) => new()
    {
        Id = name, CardName = name, Finished = finishedLocal.ToUniversalTime(), Bytes = bytes, Success = success, Verified = verified, FailedFiles = failed,
        Destinations = [new HistoryDestination { Root = @"D:\A" }, new HistoryDestination { Root = @"F:\B" }],
    };

    [Fact]
    public void Today_RowsAndSummary_AsTheDesign()
    {
        var now = new DateTime(2026, 10, 7, 20, 0, 0, DateTimeKind.Local);
        var entries = new[]
        {
            Entry("SND_DAY03", now.Date.AddHours(14).AddMinutes(5), 38_000_000_000),
            Entry("A034_ARRI", now.Date.AddHours(18).AddMinutes(12), 256_000_000_000),
            Entry("B011_FX6", now.Date.AddHours(16).AddMinutes(47), 118_000_000_000),
            Entry("YESTERDAY", now.Date.AddHours(-2), 1_000_000_000),
        };

        var today = HistoryView.Today(entries, now)!;

        Assert.Equal("3 karty · 412 GB · vše ověřeno", today.Summary);
        Assert.Equal([("A034_ARRI", "256 GB", "18:12", "✓ 2 kopie ověřeny"), ("B011_FX6", "118 GB", "16:47", "✓ 2 kopie ověřeny"),
                      ("SND_DAY03", "38,0 GB", "14:05", "✓ 2 kopie ověřeny")],
            today.Rows.Select(r => (r.CardName, r.Size, r.Time, r.Status)));
        Assert.Equal(["Dnes", "Včera"], HistoryView.Days(entries, now).Select(d => d.Title));
        Assert.Null(HistoryView.Today([entries[3]], now));
    }

    [Fact]
    public void Rows_ErrorsAndUnverified_OlderDays()
    {
        var now = new DateTime(2026, 10, 7, 20, 0, 0, DateTimeKind.Local);
        var entries = new[]
        {
            Entry("A", now.AddHours(-1), 2_000_000_000, success: false, failed: 3),
            Entry("B", now.AddHours(-2), 16_000_000_000, verified: false),
            Entry("C", now.AddDays(-3), 1_000_000_000),
        };
        var days = HistoryView.Days(entries, now);
        Assert.Equal(["Dnes", "4. 10. 2026"], days.Select(d => d.Title));
        Assert.Equal("2 karty · 18,0 GB · 1 karta s chybami · 1 neověřená", days[0].Summary);
        Assert.Equal([("✕ s chybami (3 soubory)", HistoryTone.Error), ("! neověřeno", HistoryTone.Warning)],
            days[0].Rows.Select(r => (r.Status, r.Tone)));
    }
    // The history entry already holds the backup's state as the reports say it (BackupState / CardRecord, ruling R2): the row
    // shows its flags and never works them out again — a repaired backup is "ověřeno" whatever its first generation said.
    [Fact]
    public void Row_ShowsTheEntrysSharedState_NotItsGenerations()
    {
        var e = Entry("R", new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Local), 5_000_000_000);
        e.Generations = [new HistoryGeneration { Seq = 1, Kind = HistoryGeneration.Full, Verified = false, Finished = e.Finished },
                         new HistoryGeneration { Seq = 2, Kind = HistoryGeneration.Repair, Verified = true, Finished = e.Finished }];
        Assert.Equal(("✓ 2 kopie ověřeny", HistoryTone.Ok), (HistoryView.Row(e).Status, HistoryView.Row(e).Tone));
        e.Verified = false;
        Assert.Equal(("! neověřeno", HistoryTone.Warning), (HistoryView.Row(e).Status, HistoryView.Row(e).Tone));
        Assert.Equal(("5,00 GB", "R"), (HistoryView.Row(e).Size, HistoryView.Row(e).CardName));
    }
}

/// 0.5.2: the rows of "3 · Po zkopírování" are clickable — which section each opens, which has a switch, when it is locked.
public class AfterCopyRulesTests
{
    static readonly LutCatalog Cat = LutCatalog.Load(LutCatalog.DefaultFolder);

    [Fact]
    public void Rows_CarryTheirKindAndSection()
    {
        var rows = SetupTexts.AfterCopy(new SettingsProfile(), Cat, null);
        Assert.Equal([AfterCopyKind.Verify, AfterCopyKind.Analysis, AfterCopyKind.Proxy, AfterCopyKind.Report], rows.Select(r => r.Kind));
        Assert.Equal(["verify", "analysis", "proxy", "report"], rows.Select(r => r.Section));
        Assert.Equal([SettingsSections.Verify, SettingsSections.Analysis, SettingsSections.Proxy, SettingsSections.Report], rows.Select(r => r.Section));
        Assert.Equal("folders", SettingsSections.Folders);
    }

    [Fact]
    public void OnlyReportHasNoSwitch()
    {
        var rows = SetupTexts.AfterCopy(new SettingsProfile(), Cat, null);
        Assert.Equal([true, true, true, false], rows.Select(r => r.HasSwitch));
        Assert.Equal("Report se vytváří vždy", rows[3].SwitchTip);
        Assert.Null(rows[0].SwitchTip);
    }

    [Theory]
    [InlineData(AfterCopyKind.Verify, true, true, true)]
    [InlineData(AfterCopyKind.Analysis, true, true, true)]
    [InlineData(AfterCopyKind.Proxy, true, true, true)]
    [InlineData(AfterCopyKind.Report, true, true, false)]      // the report is always written
    [InlineData(AfterCopyKind.Verify, false, true, false)]     // Start is running
    [InlineData(AfterCopyKind.Proxy, true, false, false)]      // demo: nothing is written
    public void CanToggle_Rules(AfterCopyKind kind, bool canSwitchProject, bool settingsSaveAllowed, bool expected) =>
        Assert.Equal(expected, AfterCopyRules.CanToggle(kind, canSwitchProject, settingsSaveAllowed));

    [Fact]
    public void Toggle_FlipsTheProfilesSetting()
    {
        var p = new SettingsProfile();
        Assert.True(AfterCopyRules.Toggle(p, AfterCopyKind.Verify)); Assert.False(p.Verify);
        Assert.True(AfterCopyRules.Toggle(p, AfterCopyKind.Analysis)); Assert.False(p.AnalyzeClips);
        Assert.True(AfterCopyRules.Toggle(p, AfterCopyKind.Proxy)); Assert.True(p.Proxy.Enabled);
        Assert.True(AfterCopyRules.Toggle(p, AfterCopyKind.Verify)); Assert.True(p.Verify);
        Assert.False(AfterCopyRules.Toggle(p, AfterCopyKind.Report));
    }

    [Fact]
    public void Details_FollowTheProfile()
    {
        var p = new SettingsProfile { Hash = "both" };
        Assert.Equal("xxHash64 + MD5", SetupTexts.AfterCopy(p, Cat, null)[0].Value);
        AfterCopyRules.Toggle(p, AfterCopyKind.Verify);
        var r = SetupTexts.AfterCopy(p, Cat, null)[0];
        Assert.Equal(("vypnuto", false, true), (r.Value, r.On, r.Warn));
    }
}
