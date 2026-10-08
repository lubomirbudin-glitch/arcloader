using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Verification;

namespace Offload.App;

/// Nástroje → Ověřit zálohu… (spec 2.4): a card folder or a whole day; each card is one item in the copy queue.
public sealed partial class MainViewModel
{
    bool saveVerifyReport = true;

    public RelayCommand VerifyBackupCommand { get; private set; } = null!;

    /// "Ukládat report ověření" (menu, on by default): the HTML/PDF/JSON go into the card's _REPORT folder.
    public bool SaveVerifyReport
    {
        get => saveVerifyReport;
        set => Set(ref saveVerifyReport, value);
    }

    void InitVerify() => VerifyBackupCommand = new RelayCommand(PickAndVerify, () => !closing && Allowed(DemoCommand.VerifyBackup));

    async void PickAndVerify()
    {
        if (!Allowed(DemoCommand.VerifyBackup)) return;
        if (Dialogs.PickFolder("Vyberte složku karty na cíli (nebo celý den)") is not string folder) return;
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
                var item = new VerifyItemViewModel(this, new VerifyJob(card, SaveVerifyReport, m => AppLog.Write($"{card}: {m}")));
                Jobs.Insert(0, item);
                queue.Enqueue(item.Job);
            }
            RaiseMany(nameof(HasJobs), nameof(HasFinished));
        }
        catch (Exception e)
        {
            AppLog.Write($"verify: {e}");
            ShowMessage($"Ověření se nepodařilo spustit: {e.Message}");
        }
    }
}
