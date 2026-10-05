using System.Windows;
using Microsoft.Win32;

namespace Offload.App;

static class Dialogs
{
    static Window? Owner => Application.Current?.MainWindow;

    public static string? PickFolder(string title)
    {
        var d = new OpenFolderDialog { Title = title, Multiselect = false };
        return (Owner is { } o ? d.ShowDialog(o) : d.ShowDialog()) == true ? d.FolderName : null;
    }

    public static string? PickFile(string title, string filter)
    {
        var d = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return (Owner is { } o ? d.ShowDialog(o) : d.ShowDialog()) == true ? d.FileName : null;
    }
}
