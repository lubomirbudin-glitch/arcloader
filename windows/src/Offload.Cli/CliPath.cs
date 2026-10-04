namespace Offload.Cli;

static class CliPath
{
    /// Full path; a bare drive letter such as `E:` means the drive root, not the current directory on that drive.
    public static string Full(string p) => Path.GetFullPath(p.Length == 2 && p[1] == ':' ? p + "\\" : p);
}
