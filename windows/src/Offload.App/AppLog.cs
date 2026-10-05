using System.Globalization;
using System.IO;
using Offload.Core.Desktop;

namespace Offload.App;

/// %APPDATA%\Offload\app.log - the app's own problems (job details are in each job's report).
static class AppLog
{
    static readonly Lock Gate = new();

    public static string Path => System.IO.Path.Combine(AppData.Folder, "app.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppData.Folder);
                File.AppendAllText(Path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message + Environment.NewLine);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Logging must never fail the app.
        }
    }
}
