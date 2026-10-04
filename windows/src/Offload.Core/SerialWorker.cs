using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Offload.Core;

/// One thread working through a FIFO queue — the counterpart of a serial DispatchQueue on the Mac.
internal sealed class SerialWorker : IDisposable
{
    readonly BlockingCollection<Action> queue = new();
    readonly Thread thread;

    public SerialWorker(string name)
    {
        thread = new Thread(Run) { IsBackground = true, Name = name };
        thread.Start();
    }

    void Run()
    {
        foreach (var action in queue.GetConsumingEnumerable())
        {
            try { action(); }
            catch (Exception) { /* queued actions record their own errors; one must never stop the queue */ }
        }
    }

    public void Post(Action action) => queue.Add(action);

    /// Runs `action` after everything queued so far and waits for it; its exception is rethrown here.
    public void Invoke(Action action)
    {
        using var done = new ManualResetEventSlim();
        ExceptionDispatchInfo? error = null;
        queue.Add(() =>
        {
            try { action(); }
            catch (Exception e) { error = ExceptionDispatchInfo.Capture(e); }
            finally { done.Set(); }
        });
        done.Wait();
        error?.Throw();
    }

    public void Wait() => Invoke(() => { });

    public void Dispose()
    {
        queue.CompleteAdding();
        thread.Join();
        queue.Dispose();
    }
}
