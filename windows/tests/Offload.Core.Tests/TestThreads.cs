namespace Offload.Core.Tests;

/// Work a test waits on runs on its own thread, not on the thread pool: under the whole suite (ffmpeg tests, parallel
/// classes) the pool can be busy for seconds, and a Task.Run started that late fails a test's timeout.
static class TestThreads
{
    public static Task<T> Start<T>(Func<T> work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    public static Task Start(Action work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
}
