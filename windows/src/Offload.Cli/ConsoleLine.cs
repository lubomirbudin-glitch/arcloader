namespace Offload.Cli;

/// One status line on stderr that rewrites itself; silent when stderr goes to a file.
static class ConsoleLine
{
    public static void Write(string text)
    {
        if (Console.IsErrorRedirected) return;
        try
        {
            int width = Math.Max(20, Console.WindowWidth - 1);
            Console.Error.Write("\r" + (text.Length > width ? text[..width] : text.PadRight(width)));
        }
        catch (IOException)
        {
            // No console window (e.g. started from a service): progress is optional.
        }
    }

    public static void Clear()
    {
        Write("");
        if (!Console.IsErrorRedirected) Console.Error.Write("\r");
    }
}
