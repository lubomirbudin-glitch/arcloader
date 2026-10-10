using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Outputs;
using Offload.Core.Verification;

namespace Offload.App;

/// Tools → Verify backup… (spec 2.4): a card folder or a whole day; each card is one item in the copy queue.
public sealed partial class MainViewModel
{
    bool saveVerifyReport = true;

    public RelayCommand VerifyBackupCommand { get; private set; } = null!;

    /// "Save a verification report" (menu, on by default): the HTML/PDF/JSON go into the card's _REPORT folder.
    public bool SaveVerifyReport
    {
        get => saveVerifyReport;
        set => Set(ref saveVerifyReport, value);
    }

    void InitVerify() => VerifyBackupCommand = new RelayCommand(PickAndVerify, () => !closing && Allowed(DemoCommand.VerifyBackup));

    async void PickAndVerify()
    {
        if (!Allowed(DemoCommand.VerifyBackup)) return;
        if (Dialogs.PickFolder("Choose a card folder on a destination (or a whole day)") is not string folder) return;
        try
        {
            var cards = await Task.Run(() => VerifyTargets.Resolve(folder));   // the folder may be on a slow or network disk
            if (closing) return;
            if (cards.Count == 0)
            {
                ShowMessage(Msg.NoHashRecord(folder));
                return;
            }
            foreach (var card in cards.Reverse())   // the first card ends up on top
            {
                var item = new VerifyItemViewModel(this, new VerifyJob(card, SaveVerifyReport, m => AppLog.Write($"{card}: {m}"),
                    LocalReports.RootFor(Settings.Outputs)));
                Jobs.Insert(0, item);
                queue.Enqueue(item.Job);
            }
            RaiseMany(nameof(HasJobs), nameof(HasFinished));
        }
        catch (Exception e)
        {
            AppLog.Write($"verify: {e}");
            ShowMessage($"Could not start the verification: {e.Message}");
        }
    }
}
