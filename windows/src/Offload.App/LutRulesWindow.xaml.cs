using System.Windows;
using Offload.Core;

namespace Offload.App;

/// "Vlastní pravidla LUTů" (spec 4.2). OK takes the rules only when every row is complete (Problem says what is missing).
public partial class LutRulesWindow : Window
{
    readonly LutRulesViewModel vm;
    List<LutRule>? result;

    LutRulesWindow(LutRulesViewModel vm)
    {
        InitializeComponent();
        this.vm = vm;
        DataContext = vm;
    }

    /// The edited rules, or null when the user cancelled.
    public static List<LutRule>? Edit(Window owner, IReadOnlyList<LutRule> rules, LutCatalog catalog)
    {
        var w = new LutRulesWindow(new LutRulesViewModel(rules, catalog)) { Owner = owner };
        w.ShowDialog();
        return w.result;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (vm.Result() is not { } rules) return;   // the red line says which row
        result = rules;
        DialogResult = true;
    }
}
