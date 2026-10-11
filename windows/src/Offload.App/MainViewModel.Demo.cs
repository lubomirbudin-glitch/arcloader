using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.App;

/// `ArcLoader.exe --demo <stav>` (DemoScenario, off by default): the window shows a state of docs/ui-navrh/screeny for the
/// visual check — the scans, the card inspections and history.json are replaced by the scenario's data; nothing is
/// copied, nothing is written. The destinations are the ones of settings.json (the check gives it two empty folders).
public sealed partial class MainViewModel
{
    DemoState? demo;
    readonly Stopwatch demoClock = new();
    List<VolumeItem> demoVolumes = [];

    /// True in the demo: the real scans and inspections stay off.
    bool IsDemo => demo != null;

    /// Ruling I3: in the demo every command that starts work or writes anything is off (DemoGate; the app-data folder of
    /// the demo is a temp folder of its own, see App.OnStartup).
    internal bool Allowed(DemoCommand command) => DemoGate.Allows(demo, command);

    /// Pause / Resume / Cancel work on a demo preview job, which only changes what its card shows.
    internal bool AllowedFor(DemoCommand command, bool previewJob) => DemoGate.Allows(demo, command, previewJob);

    /// The demo has no settings.json of its own to read: two destinations in its temp folder (empty, created here) and
    /// proxy on, in memory only — nothing is ever saved (DemoCommand.SettingsSave).
    void SeedDemo()
    {
        if (!IsDemo) return;
        var folder = Path.Combine(AppData.Folder, "demo");
        if (Settings.DestRoots.Count == 0)
            foreach (var disk in new[] { "RAID_A", "SHUTTLE_B" })
            {
                var root = Path.Combine(folder, disk);
                Directory.CreateDirectory(root);
                Settings.DestRoots.Add(root);
            }
        Settings.Proxy.Enabled = true;
    }

    void InitDemo()
    {
        if (demo is not { } state) return;
        demoClock.Start();
        var folder = Path.Combine(AppData.Folder, "demo");
        var roots = Settings.DestRoots.ToList();
        if (state == DemoState.Ready)
        {
            var cards = new[] { DemoScenario.Ready(folder), DemoScenario.BackedUp(folder, roots) };
            demoVolumes = cards.Select(c => new VolumeItem(c.Label, c.Path, c.Total, c.Free, c.Format, LooksLikeCard: true, IsDestination: false,
                Identity: "demo-" + c.Label, Label: c.Label)).ToList();
            foreach (var c in cards)
            {
                var identity = "demo-" + c.Label;
                if (inspections.Start(c.Path, identity) is not null) inspections.Finished(c.Path, c.Insight, identity, stillListed: true);
                sourceLinks[c.Path] = (identity, c.Connection);
            }
            history = DemoScenario.Today(roots, DateTime.Now);
            ApplyVolumes(demoVolumes);
            selected.Clear();
            selected.Add(cards[0].Path);   // A035 ticked, B012 offers Append
            RebuildSources();
        }
        else history = [];
        string Source(string card) => Path.Combine(folder, card);
        switch (state)
        {
            case DemoState.Running:
            {
                var (waiting, facts) = DemoScenario.WaitingAppend(Source("B012_FX6"), roots);
                Jobs.Insert(0, new JobItemViewModel(this, waiting, "B012_FX6", canEject: false, facts: facts));
                Jobs.Insert(0, new JobItemViewModel(this, DemoScenario.Running(Source("A035_ARRI"), roots, () => demoClock.Elapsed.TotalSeconds), "A035_ARRI",
                    canEject: false, facts: new JobCardFacts(24, 24, 170_000_000_000, "ALEXA 35", false)));
                break;
            }
            case DemoState.Finished:
                Jobs.Insert(0, new JobItemViewModel(this, DemoScenario.Mismatch(Source("B012_FX6"), roots), "B012_FX6", canEject: false));
                Jobs.Insert(0, new JobItemViewModel(this, DemoScenario.Done(Source("A035_ARRI"), roots, GreyThumbnail), "A035_ARRI", canEject: false));
                break;
            case DemoState.Verifying:
                // Plan 10 (R5): both verdicts for the screenshots — "Safe to eject" while verifying, the content defect, "Safe to format".
                Jobs.Insert(0, new JobItemViewModel(this, DemoScenario.Done(Source("A035_ARRI"), roots, GreyThumbnail), "A035_ARRI", canEject: false));
                Jobs.Insert(0, new JobItemViewModel(this, DemoScenario.ContentDefect(Source("A037_ARRI"), roots, GreyThumbnail), "A037_ARRI", canEject: false));
                Jobs.Insert(0, new JobItemViewModel(this, DemoScenario.Verifying(Source("A036_ARRI"), roots), "A036_ARRI", canEject: false,
                    facts: new JobCardFacts(24, 24, 96_000_000_000, "ALEXA 35", false)));
                break;
            case DemoState.Settings:
                SettingsSection = SettingsSections.Proxy;
                IsSettingsOpen = true;
                break;
        }
        foreach (var j in Jobs) j.Refresh();
        RaiseMany(nameof(HasJobs), nameof(HasFinished), nameof(Today), nameof(HistoryDays));
    }

    static readonly Color[] ThumbColors =
        [Color.FromRgb(0x26, 0x30, 0x2B), Color.FromRgb(0x2B, 0x2A, 0x26), Color.FromRgb(0x23, 0x2A, 0x33), Color.FromRgb(0x2E, 0x28, 0x28), Color.FromRgb(0x26, 0x2B, 0x2E)];

    /// A grey 160 × 90 JPEG in the design's thumbnail colours.
    static byte[] GreyThumbnail(int i)
    {
        var c = ThumbColors[i % ThumbColors.Length];
        var pixels = new byte[160 * 90 * 3];
        for (int p = 0; p < pixels.Length; p += 3) (pixels[p], pixels[p + 1], pixels[p + 2]) = (c.B, c.G, c.R);
        var image = BitmapSource.Create(160, 90, 96, 96, PixelFormats.Bgr24, null, pixels, 160 * 3);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
