using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace StardewModdingAPI.Mobile;

/// <summary>Queues work for deterministic one-at-a-time execution from the Android game loop.</summary>
internal sealed class AndroidMainThreadTaskQueue
{
    private readonly ConcurrentQueue<Task> pending = new();

    public Task Enqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var task = new Task(action);
        pending.Enqueue(task);
        return task;
    }

    public bool TryRunNext()
    {
        if (!pending.TryDequeue(out var task))
            return false;
        task.RunSynchronously();
        return true;
    }
}
