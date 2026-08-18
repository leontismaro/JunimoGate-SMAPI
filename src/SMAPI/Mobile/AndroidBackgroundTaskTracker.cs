using System;
using System.Threading;
using System.Threading.Tasks;

namespace StardewModdingAPI.Mobile;

/// <summary>Tracks Android background work without scanning a task collection from the game loop.</summary>
internal sealed class AndroidBackgroundTaskTracker
{
    private int pendingCount;
    private int blockingPendingCount;

    public bool HasPending => Volatile.Read(ref pendingCount) > 0;

    public bool HasBlockingPending => Volatile.Read(ref blockingPendingCount) > 0;

    internal int PendingCount => Volatile.Read(ref pendingCount);

    public Task Start(Action action)
        => this.Start(action, blockGameUpdating: true);

    public Task StartNonBlocking(Action action)
        => this.Start(action, blockGameUpdating: false);

    private Task Start(Action action, bool blockGameUpdating)
    {
        ArgumentNullException.ThrowIfNull(action);
        Interlocked.Increment(ref pendingCount);
        if (blockGameUpdating)
            Interlocked.Increment(ref blockingPendingCount);
        var task = new Task(() =>
        {
            try
            {
                action();
            }
            finally
            {
                Interlocked.Decrement(ref pendingCount);
                if (blockGameUpdating)
                    Interlocked.Decrement(ref blockingPendingCount);
            }
        });
        try
        {
            task.Start();
            return task;
        }
        catch
        {
            Interlocked.Decrement(ref pendingCount);
            if (blockGameUpdating)
                Interlocked.Decrement(ref blockingPendingCount);
            throw;
        }
    }
}
