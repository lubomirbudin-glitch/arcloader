namespace Offload.Core;

/// Safe copies of finished files (proxies, the PDF report): a temporary `.part` next to the target, then a rename.
public static class ProxyCopy
{
    /// Copies `src` to `dst` via `.<name>.part`; a failure leaves no temporary file behind.
    public static void CopyFile(string src, string dst, bool replace = true)
    {
        var dir = Path.GetDirectoryName(dst)!;
        NativeIO.Mkdirs(dir);
        var part = Path.Combine(dir, "." + Path.GetFileName(dst) + ".part");
        NativeIO.Delete(part);
        try
        {
            File.Copy(src, part);
            NativeIO.Move(part, dst, replace);
        }
        catch
        {
            NativeIO.Delete(part);
            throw;
        }
    }

    /// The extra copy of a proxy into the user's proxy folder: `<extraFolder>\<date>\<cardName>\<fileName>`, or
    /// `<name>_2.mov`, `_3` … when the name is taken there — never overwrites. `fileName` may have the clip's folder in
    /// front ("idle/1.mov", 0.5.1: the same folders as `_PROXY`); the suffix is then looked for inside that folder.
    /// Never throws: a missing folder (disconnected disk) or a failed copy comes back as the error text.
    /// At most 1000 names (`_2` … `_1000`) are tried; then it gives up with an error.
    public static (string? Path, string? Error) ToExtraFolder(string source, string extraFolder, string date, string cardName, string fileName) =>
        ToExtraFolder(source, extraFolder, date, cardName, fileName, maxAttempts: 1000);

    internal static (string? Path, string? Error) ToExtraFolder(string source, string extraFolder, string date, string cardName,
        string fileName, int maxAttempts)
    {
        try
        {
            if (!Directory.Exists(extraFolder)) return (null, "the folder is not available");
            var dir = Layout.CardPath(extraFolder, date, cardName);
            fileName = fileName.Replace('\\', '/');
            if (fileName.LastIndexOf('/') is int slash and >= 0)
            {
                dir = Layout.Join(dir, fileName[..slash]);
                fileName = fileName[(slash + 1)..];
            }
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            string Name(int n) => n == 1 ? fileName : $"{stem}_{n}{ext}";
            for (int n = 1; n <= maxAttempts; n++)
            {
                var target = Path.Combine(dir, Name(n));
                if (Path.Exists(target)) continue;
                try
                {
                    CopyFile(source, target, replace: false);
                    return (target, null);
                }
                catch (IOError) when (Path.Exists(target))
                {
                    // Taken between the check and the rename: try the next number.
                }
            }
            return (null, $"no free name ({Name(1)} … {Name(maxAttempts)})");
        }
        catch (Exception e)
        {
            return (null, e.Message);
        }
    }
}
