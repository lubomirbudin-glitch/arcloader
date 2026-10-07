using System.Windows;
using System.Windows.Controls;

namespace Offload.App;

/// "Název projektu" for Uložit jako projekt… / Přejmenovat… (spec 4.1). The name is checked as it is typed (the Czech
/// problem under the field); OK only takes a name without a problem.
public partial class ProjectNameWindow : Window
{
    readonly Func<string, string?> problem;
    string? accepted;

    ProjectNameWindow(string heading, string initial, Func<string, string?> problem)
    {
        InitializeComponent();
        this.problem = problem;
        Heading.Text = heading;
        NameBox.Text = initial;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
        ShowProblem(problem(initial), quiet: true);
    }

    /// The accepted name (trimmed), or null when the user cancelled.
    public static string? Ask(Window owner, string heading, string initial, Func<string, string?> problem)
    {
        var w = new ProjectNameWindow(heading, initial, problem) { Owner = owner };
        w.ShowDialog();
        return w.accepted;
    }

    /// `quiet`: before the user typed anything an empty name is not shouted at — OK just stays off.
    void ShowProblem(string? text, bool quiet)
    {
        OkButton.IsEnabled = text == null;
        Problem.Text = text ?? "";
        Problem.Visibility = text != null && !quiet ? Visibility.Visible : Visibility.Collapsed;
    }

    void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ShowProblem(problem(NameBox.Text), quiet: false);
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (problem(NameBox.Text) is string text)
        {
            ShowProblem(text, quiet: false);
            return;
        }
        accepted = NameBox.Text.Trim();
        DialogResult = true;
    }
}
