namespace Offload.Core.Desktop;

/// What the window does that starts work or writes something (ruling I3). In `--demo` all of it is off; the window asks
/// here before each one. Nothing is exempt but the in-memory Pozastavit / Pokračovat / Zrušit of a preview job (it never
/// runs, so they only change what the card shows — the screenshot of a paused card).
public enum DemoCommand
{
    Start, Append, Repair, Pause, Resume, Cancel, MeasurePorts, ProjectEdit, SettingsSave, UpdateCheck, Notify, VerifyBackup, DayReport, Eject, SummaryReport,
}

public static class DemoGate
{
    /// `demo` null = a normal run: everything is allowed. `previewJob`: the job is a DemoScenario preview.
    public static bool Allows(DemoState? demo, DemoCommand command, bool previewJob = false) =>
        demo == null || (previewJob && command is DemoCommand.Pause or DemoCommand.Resume or DemoCommand.Cancel);
}
