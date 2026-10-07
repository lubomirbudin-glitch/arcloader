using System.Windows;
using System.Windows.Controls;
using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.App;

/// One card in the Fronta panel: an offload (JobItemViewModel) or a verification (VerifyItemViewModel).
public abstract class QueueItem : ObservableObject
{
    /// What the JobQueue runs.
    public abstract IQueuedJob Queued { get; }
    /// As a copy phase — a running verification counts as Verifying (no speed measurement meanwhile).
    public abstract JobPhase Phase { get; }
    public abstract bool IsFinished { get; }
    /// Pozastaveno (the top bar's pill counts it apart); only an offload can be paused.
    public virtual bool IsPaused => false;
    public virtual bool IsPauseHeld => false;
    /// Failed or with problems: the error sound and flash.
    public abstract bool IsError { get; }
    /// Still needs the app when closing (not finished, or an eject in flight).
    public virtual bool Busy => !IsFinished;
    /// The attention flash and sound already came at the end of the copy (QueueRules.AlertAtCopyEnd).
    public bool Alerted { get; set; }
    /// The end-of-job notification (Windows / ntfy) was already sent (QueueRules.NotifyAtCopyEnd).
    public bool Notified { get; set; }
    public abstract void Refresh();
    public abstract void Cancel();
}

/// QueuePanel: an offload card or a verification card.
public sealed class QueueItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? JobTemplate { get; set; }
    public DataTemplate? VerifyTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is VerifyItemViewModel ? VerifyTemplate : JobTemplate;
}
