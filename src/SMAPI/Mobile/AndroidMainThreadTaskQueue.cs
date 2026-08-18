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
    private readonly Func<string?, IDisposable?>? trackWork;
    private int gameThreadId;

    internal bool HasPending => !this.pending.IsEmpty;

    public AndroidMainThreadTaskQueue(Func<string?, IDisposable?>? trackWork = null)
    {
        this.trackWork = trackWork;
    }

    public Task Enqueue(Action action, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Volatile.Read(ref this.gameThreadId) == Environment.CurrentManagedThreadId)
        {
            IDisposable? tracking = this.TryBeginTracking(name);
            try
            {
                action();
                return Task.CompletedTask;
            }
            catch (Exception exception)
            {
                return Task.FromException(exception);
            }
            finally
            {
                TryDisposeTracking(tracking);
            }
        }

        return this.EnqueueDeferred(action, name);
    }

    /// <summary>Queue work for a later pump even when called from the game thread.</summary>
    public Task EnqueueDeferred(Action action, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
            IDisposable? tracking = this.TryBeginTracking(work.Name);
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
                TryDisposeTracking(tracking);
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

    private IDisposable? TryBeginTracking(string? name)
    {
        try
        {
            return this.trackWork?.Invoke(name);
        }
        catch
        {
            return null;
        }
    }

    private static void TryDisposeTracking(IDisposable? tracking)
    {
        try
        {
            tracking?.Dispose();
        }
        catch
        {
            // Diagnostics must not affect queued work.
        }
    }

    internal readonly record struct PumpResult(int ExecutedItems, TimeSpan Elapsed, bool HasPending);

    private sealed record WorkItem(Action Action, string? Name, TaskCompletionSource Completion);
}
