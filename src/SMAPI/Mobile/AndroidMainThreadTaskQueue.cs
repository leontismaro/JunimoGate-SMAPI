using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace StardewModdingAPI.Mobile;

/// <summary>Queues work for bounded execution from the Android game thread.</summary>
internal sealed class AndroidMainThreadTaskQueue
{
    private readonly ConcurrentQueue<WorkItem> pending = new();
    private int gameThreadId;

    public Task Enqueue(Action action, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Volatile.Read(ref this.gameThreadId) == Environment.CurrentManagedThreadId)
        {
            try
            {
                action();
                return Task.CompletedTask;
            }
            catch (Exception exception)
            {
                return Task.FromException(exception);
            }
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        this.pending.Enqueue(new WorkItem(action, name, completion));
        return completion.Task;
    }

    public PumpResult Pump(
        TimeSpan timeBudget,
        int maxItems,
        Action<string?, TimeSpan>? onCompleted = null)
    {
        if (timeBudget < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeBudget));
        if (maxItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxItems));

        int currentThreadId = Environment.CurrentManagedThreadId;
        int ownerThreadId = Interlocked.CompareExchange(ref this.gameThreadId, currentThreadId, 0);
        if (ownerThreadId != 0 && ownerThreadId != currentThreadId)
            throw new InvalidOperationException("The Android main-thread queue was pumped from multiple threads.");

        long pumpStartedAt = Stopwatch.GetTimestamp();
        int executed = 0;
        while (executed < maxItems && this.pending.TryDequeue(out WorkItem? work))
        {
            long workStartedAt = Stopwatch.GetTimestamp();
            try
            {
                work.Action();
                work.Completion.TrySetResult();
            }
            catch (Exception exception)
            {
                work.Completion.TrySetException(exception);
            }
            finally
            {
                executed++;
                onCompleted?.Invoke(work.Name, Stopwatch.GetElapsedTime(workStartedAt));
            }

            if (Stopwatch.GetElapsedTime(pumpStartedAt) >= timeBudget)
                break;
        }

        return new PumpResult(executed, Stopwatch.GetElapsedTime(pumpStartedAt), !this.pending.IsEmpty);
    }

    public void Reset(Exception? reason = null)
    {
        reason ??= new OperationCanceledException("The Android main-thread work queue was reset.");
        while (this.pending.TryDequeue(out WorkItem? work))
            work.Completion.TrySetException(reason);
        Volatile.Write(ref this.gameThreadId, 0);
    }

    internal readonly record struct PumpResult(int ExecutedItems, TimeSpan Elapsed, bool HasPending);

    private sealed record WorkItem(Action Action, string? Name, TaskCompletionSource Completion);
}
