using System;
using System.Threading;
using System.Threading.Tasks;

namespace StardewModdingAPI.Mobile;

/// <summary>Tracks Android background work without scanning a task collection from the game loop.</summary>
internal sealed class AndroidBackgroundTaskTracker
{
    private int pendingCount;

    public bool HasPending => Volatile.Read(ref pendingCount) > 0;

    internal int PendingCount => Volatile.Read(ref pendingCount);

    public Task Start(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Interlocked.Increment(ref pendingCount);
        var task = new Task(() =>
        {
            try
            {
                action();
            }
            finally
            {
                Interlocked.Decrement(ref pendingCount);
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
            throw;
        }
    }
}
