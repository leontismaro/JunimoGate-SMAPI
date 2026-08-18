using System;
using System.Threading;
using System.Threading.Tasks;

namespace StardewModdingAPI.Mobile;

/// <summary>Tracks Android background work without scanning a task collection from the game loop.</summary>
internal sealed class AndroidBackgroundTaskTracker
{
    private int blockingPendingCount;

    public bool HasBlockingPending => Volatile.Read(ref blockingPendingCount) > 0;

    public Task Start(Action action)
        => this.Start(action, blockGameUpdating: true);

    public Task StartNonBlocking(Action action)
        => this.Start(action, blockGameUpdating: false);

    private Task Start(Action action, bool blockGameUpdating)
    {
        ArgumentNullException.ThrowIfNull(action);
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
            if (blockGameUpdating)
                Interlocked.Decrement(ref blockingPendingCount);
            throw;
        }
    }
}
