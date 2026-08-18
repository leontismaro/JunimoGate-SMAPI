using System;
using System.Threading.Tasks;

namespace StardewModdingAPI.Mobile;

/// <summary>Runs at most one queued item between completed game draws.</summary>
internal sealed class AndroidDrawGatedTaskQueue
{
    private readonly AndroidMainThreadTaskQueue queue;
    private bool isAllowed = true;

    public AndroidDrawGatedTaskQueue(Func<string?, IDisposable?>? trackWork = null)
    {
        this.queue = new AndroidMainThreadTaskQueue(trackWork);
    }

    public Task Enqueue(Action action, string? name = null)
        => this.queue.EnqueueDeferred(action, name);

    public AndroidMainThreadTaskQueue.PumpResult Pump(
        TimeSpan timeBudget,
        Action<string?, TimeSpan>? onCompleted = null)
    {
        if (!this.isAllowed)
            return new AndroidMainThreadTaskQueue.PumpResult(0, TimeSpan.Zero, this.queue.HasPending);

        AndroidMainThreadTaskQueue.PumpResult result = this.queue.Pump(timeBudget, maxItems: 1, onCompleted);
        if (result.ExecutedItems > 0)
            this.isAllowed = false;
        return result;
    }

    public void ReleaseAfterDraw()
    {
        this.isAllowed = true;
    }

    public void Close(Exception? reason = null)
    {
        this.queue.Close(reason);
    }
}
