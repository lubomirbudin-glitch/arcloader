using System.Windows;
using Offload.Core.Desktop;

namespace Offload.App;

/// "Cards already backed up" before a full copy (spec 2.1). Own window: MessageBox cannot have three own buttons.
public partial class BackedUpPromptWindow : Window
{
    BackedUpChoice choice = BackedUpChoice.Cancel;   // closed with the X or Esc = nothing starts

    BackedUpPromptWindow(BackedUpPromptModel model)
    {
        InitializeComponent();
        DataContext = model;
        CopyAgainButton.Content = BackedUpPrompt.CopyAgainText;
        SkipButton.Content = BackedUpPrompt.SkipText;
        CancelButton.Content = BackedUpPrompt.CancelText;
    }

    public static BackedUpChoice Ask(Window owner, BackedUpPromptModel model)
    {
        var w = new BackedUpPromptWindow(model) { Owner = owner };
        w.ShowDialog();
        return w.choice;
    }

    void CopyAgain_Click(object sender, RoutedEventArgs e)
    {
        choice = BackedUpChoice.CopyAgain;
        Close();
    }

    void Skip_Click(object sender, RoutedEventArgs e)
    {
        choice = BackedUpChoice.SkipBackedUp;
        Close();
    }
}
