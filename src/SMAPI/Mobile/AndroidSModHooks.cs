using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI.Framework;
using StardewModdingAPI.Internal;
using StardewValley.Extensions;

namespace StardewModdingAPI.Mobile;

[HarmonyPatch]
internal static class AndroidSModHooks
{
    static IMonitor Monitor => SCore.Instance.SMAPIMonitor;
    private static readonly AndroidBackgroundTaskTracker BackgroundTasks = new();
    private static readonly AndroidMainThreadTaskQueue MainThreadTasks = new(
        name => AndroidRuntimeDiagnostics.Track("main-thread-task", name ?? "<unnamed>"));
    private static readonly AndroidDrawGatedTaskQueue AudioMainThreadTasks = new(
        name => AndroidRuntimeDiagnostics.Track("audio-main-thread-task", name ?? "<unnamed>"));
    private static readonly TimeSpan MainThreadBudget = TimeSpan.FromMilliseconds(32);

    internal static void Init()
    {
        AndroidRuntimeDiagnostics.Start(Monitor);
        AndroidGameLoopManager.RegisterOnGameUpdating(OnGameUpdating_TaskUpdate);
        AndroidSaveLoaderManager.Init();
        SGameRunner.RegisterOnDraw(OnAndroidDraw);
    }

    internal static bool OnGameUpdating_TaskUpdate(GameTime time)
    {

        AndroidMainThreadTaskQueue.PumpResult pump = PumpMainThreadTasks();
        AndroidMainThreadTaskQueue.PumpResult audioPump = PumpAudioMainThreadTasks();
        // Audio cue publication mutates MonoGame-owned objects on this thread, but it is
        // deliberately not a global update barrier. The title and its input must keep
        // advancing while background Vorbis decode is still producing the next cue.
        bool markSkipGameUpdating = pump.ExecutedItems > 0 || audioPump.ExecutedItems > 0;

        if (BackgroundTasks.HasBlockingPending)
            markSkipGameUpdating = true;

        return markSkipGameUpdating;
    }
    internal static Task AddTaskRunOnMainThread(Action callback, string? name = null)
        => MainThreadTasks.Enqueue(callback, name);

    internal static Task AddAudioTaskRunOnMainThreadDeferred(Action callback, string? name = null)
        => AudioMainThreadTasks.Enqueue(callback, name);

    internal static AndroidMainThreadTaskQueue.PumpResult PumpMainThreadTasks()
    {
        AndroidMainThreadTaskQueue.PumpResult result = MainThreadTasks.Pump(
            MainThreadBudget,
            int.MaxValue,
            static (name, elapsed) =>
            {
                if (name is not null)
                    Monitor.Log($"Done taskOnMainThread: '{name}' in {elapsed.TotalMilliseconds:F3}ms");
                if (elapsed > TimeSpan.FromSeconds(2))
                    Monitor.Log($"Main-thread task '{name ?? "<unnamed>"}' took {elapsed.TotalMilliseconds:F3}ms.", LogLevel.Warn);
            });
        return result;
    }

    internal static AndroidMainThreadTaskQueue.PumpResult PumpAudioMainThreadTasks()
    {
        AndroidMainThreadTaskQueue.PumpResult result = AudioMainThreadTasks.Pump(
            MainThreadBudget,
            static (name, elapsed) =>
            {
                if (name is not null)
                    Monitor.Log($"Done taskOnMainThread: '{name}' in {elapsed.TotalMilliseconds:F3}ms");
                if (elapsed > TimeSpan.FromSeconds(2))
                    Monitor.Log($"Main-thread task '{name ?? "<unnamed>"}' took {elapsed.TotalMilliseconds:F3}ms.", LogLevel.Warn);
            });
        return result;
    }

    internal static void CancelPendingMainThreadTasks(Exception reason)
    {
        MainThreadTasks.Close(reason);
        AudioMainThreadTasks.Close(reason);
    }

    private static void OnAndroidDraw(GameTime time)
    {
        AudioMainThreadTasks.ReleaseAfterDraw();
    }
    internal static Task StartTaskBackground(Action callback, string nameID)
    {
        Monitor.Log($"Try StartTask name: '{nameID}' on Android SModHook");
        return StartTaskBackgroundCore(callback, nameID, blockGameUpdating: true);
    }

    internal static Task StartTaskBackgroundNonBlocking(Action callback, string nameID)
    {
        Monitor.Log($"Try StartTask name: '{nameID}' on Android SModHook (non-blocking)");
        return StartTaskBackgroundCore(callback, nameID, blockGameUpdating: false);
    }

    internal static Task StartTaskBackground(Task gameTask, string nameID)
    {
        Monitor.Log($"Try StartTask name: '{nameID}' on Android SModHook");
        return StartTaskBackgroundCore(() => gameTask.RunSynchronously(), nameID, blockGameUpdating: true);
    }

    private static Task StartTaskBackgroundCore(Action callback, string nameID, bool blockGameUpdating)
    {
        ArgumentNullException.ThrowIfNull(callback);
#if false
        //debug only
        Console.WriteLine("Debug try start task background in main thread");
        var st = Stopwatch.StartNew();
        gameTask.RunSynchronously();
        st.Stop();
        Console.WriteLine($"done task: {nameID} in {st.Elapsed.TotalMilliseconds}ms");
        return gameTask;
#endif

        //setup
        Action trackedAction = () =>
        {
            try
            {
                var startedAt = Stopwatch.GetTimestamp();
                Monitor.Log($"Starting Task On Background id: '{nameID}'");
                callback();
                var elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                Monitor.Log($"Completed Task On Background id: {nameID} in {elapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Monitor.Log($"Exception on task id: {nameID}");
                Monitor.Log($"{ex.GetLogSummary()}");
            }
        };
        return blockGameUpdating
            ? BackgroundTasks.Start(trackedAction)
            : BackgroundTasks.StartNonBlocking(trackedAction);
    }

}
