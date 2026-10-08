using System.IO;
using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.App;

/// One content warning of a finished card (spec 7, 9): "A001C003" and the yellow label "01:00:02:13 · poškozená data",
/// with Náhled when the report has a picture of the place.
public sealed class ContentRowViewModel(ContentLine line, byte[]? jpeg, bool canOpen, Action<byte[]> open)
{
    public string Clip => line.Clip;
    public string Text => line.Label;
    public bool HasPicture => line.HasPicture;
    public RelayCommand PreviewCommand { get; } = new(() => { if (jpeg != null) open(jpeg); }, () => jpeg != null && canOpen);
}

/// Plan 10 on the job card (R5, spec 7, spec 9): the two verdicts "Lze vyjmout" and "Lze formátovat" side by side, never
/// merged, and the content warnings with their place and a picture — formatting waits for the user's confirmation.
public sealed partial class JobItemViewModel
{
    bool contentConfirmed;
    IReadOnlyList<ContentRowViewModel>? contentRows;

    public RelayCommand ConfirmContentCommand { get; private set; } = null!;

    void InitVerify() => ConfirmContentCommand = new RelayCommand(() =>
    {
        contentConfirmed = true;   // only unlocks the green verdict of this card — Offload never formats a card
        Refresh();
    }, () => NeedsContentConfirm);

    /// The chips show once the job has a plan (not while it waits).
    public bool ShowChips => Phase != JobPhase.Waiting && Job.Plan != null;
    public CardChip EjectChip => JobCard.Chips(progress.Status, contentConfirmed).Eject;
    public CardChip FormatChip => JobCard.Chips(progress.Status, contentConfirmed).Format;
    /// "Zkontrolováno — kartu lze formátovat" is offered while content warnings wait for a look.
    public bool NeedsContentConfirm => JobCard.NeedsContentConfirm(progress.Status, contentConfirmed);
    public string ContentConfirmText => JobCard.ContentConfirmText;

    /// The content warnings of the finished job, from its report in memory (nothing while it runs, nothing read from a disk).
    public IReadOnlyList<ContentRowViewModel> ContentRows
    {
        get
        {
            if (contentRows != null || !IsFinished || Job.Report is not { } r) return contentRows ?? [];
            var pictures = r.ContentWarnings.Select(c => c.Content!.Jpeg).ToList();
            // A demo preview writes nothing (ruling I3 of plan 9): its Náhled stays off.
            return contentRows = JobCard.ContentLines(r).Select((line, i) => new ContentRowViewModel(line, pictures[i], !Job.IsPreview, OpenDefect)).ToList();
        }
    }

    /// The picture of the defect, written to a temporary file off the UI thread and opened in the system viewer.
    async void OpenDefect(byte[] jpeg)
    {
        try
        {
            var path = await Task.Run(() =>
            {
                var p = Path.Combine(Path.GetTempPath(), $"offload-vada-{Guid.NewGuid():N}.jpg");
                File.WriteAllBytes(p, jpeg);
                return p;
            });
            if (Shell.Open(path) is string error) owner.ShowMessage(error);
        }
        catch (Exception e)
        {
            owner.ShowMessage($"Náhled nejde otevřít: {e.Message}");
        }
    }
}
