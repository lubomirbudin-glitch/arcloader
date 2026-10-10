using System.Windows;
using Offload.Core.Notify;

namespace Offload.App;

/// Tools → Notifications… (spec 4.3). Edits a copy; OK hands it back only when ntfy (if on) has a usable server and topic.
public partial class NotifyWindow : Window
{
    readonly NotifyOptionsViewModel vm;
    NotifySettings? result;

    NotifyWindow(NotifyOptionsViewModel vm)
    {
        InitializeComponent();
        this.vm = vm;
        DataContext = vm;
    }

    public static NotifySettings? Edit(Window owner, NotifySettings copy, string? project)
    {
        var w = new NotifyWindow(new NotifyOptionsViewModel(copy, project)) { Owner = owner };
        w.ShowDialog();
        return w.result;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (vm.Accept() is not { } settings) return;
        result = settings;
        DialogResult = true;
    }
}

/// The dialog's state. "Send a test notification" runs off the UI thread (await) — the window never waits on the network.
public sealed class NotifyOptionsViewModel : ObservableObject
{
    readonly NotifySettings s;
    string? testResult, problem;
    bool testing;

    public NotifyOptionsViewModel(NotifySettings copy, string? project)
    {
        s = copy;
        ProfileText = project == null ? "Applies without a project." : $"Applies to project {project}.";
        TestCommand = new RelayCommand(Test, () => !testing);
    }

    public string ProfileText { get; }
    public RelayCommand TestCommand { get; }

    public bool Windows { get => s.Windows; set { s.Windows = value; Raise(); } }
    public bool NtfyEnabled { get => s.Ntfy.Enabled; set { s.Ntfy.Enabled = value; Problem = null; Raise(); } }
    public string Server { get => s.Ntfy.Server; set { s.Ntfy.Server = value ?? ""; Problem = null; Raise(); } }
    public string Topic { get => s.Ntfy.Topic; set { s.Ntfy.Topic = value ?? ""; Problem = null; Raise(); } }

    public string? TestResult { get => testResult; private set => Set(ref testResult, value); }
    public string? Problem { get => problem; private set => Set(ref problem, value); }

    /// The settings to keep, or null (Problem says why): ntfy on needs an http(s) server and a usable topic.
    public NotifySettings? Accept()
    {
        s.Ntfy.Server = s.Ntfy.Server.Trim();
        s.Ntfy.Topic = s.Ntfy.Topic.Trim();
        if (string.IsNullOrEmpty(s.Ntfy.Server)) s.Ntfy.Server = NtfySettings.DefaultServer;
        Problem = NtfyClient.SettingsProblem(s.Ntfy);
        return Problem == null ? s : null;
    }

    async void Test()
    {
        if (testing) return;
        testing = true;
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();   // the button is disabled at once: no double send
        TestResult = "Sending…";
        try
        {
            var test = new NtfySettings { Enabled = true, Server = string.IsNullOrWhiteSpace(s.Ntfy.Server) ? NtfySettings.DefaultServer : s.Ntfy.Server.Trim(), Topic = s.Ntfy.Topic.Trim() };
            var r = await Task.Run(() => NtfyClient.SendTestAsync(test));   // M1: nothing of the send runs on the UI thread
            TestResult = r.Ok ? "Sent." : $"Failed: {r.Error}";
        }
        catch (Exception e)
        {
            TestResult = $"Failed: {e.Message}";
        }
        finally
        {
            testing = false;
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
}
