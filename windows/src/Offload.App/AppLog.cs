using System.Globalization;
using System.IO;
using Offload.Core.Desktop;

namespace Offload.App;

/// %APPDATA%\ArcLoader\app.log - the app's own problems (job details are in each job's report).
static class AppLog
{
    const long MaxBytes = 5 * 1024 * 1024;
    static readonly Lock Gate = new();

    public static string Path
    {
        get
        {
            try
            {
                return System.IO.Path.Combine(AppData.Folder, "app.log");
            }
            catch
            {
                return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ArcLoader-app.log");
            }
        }
    }

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var path = Path;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes) File.Move(path, path + ".1", overwrite: true);
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never fail the app.
        }
    }
}
