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
    private static readonly AndroidMainThreadTaskQueue MainThreadTasks = new();
    private static readonly TimeSpan MainThreadBudget = TimeSpan.FromMilliseconds(32);

    internal static void Init()
    {
        MainThreadTasks.Reset();
        AndroidGameLoopManager.RegisterOnGameUpdating(OnGameUpdating_TaskUpdate);
    }

    internal static bool OnGameUpdating_TaskUpdate(GameTime time)
    {

        AndroidMainThreadTaskQueue.PumpResult pump = PumpMainThreadTasks();
        bool markSkipGameUpdating = pump.ExecutedItems > 0;

        if (BackgroundTasks.HasPending)
            markSkipGameUpdating = true;

        return markSkipGameUpdating;
    }
    internal static Task AddTaskRunOnMainThread(Action callback, string? name = null)
        => MainThreadTasks.Enqueue(callback, name);

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

    internal static void CancelPendingMainThreadTasks(Exception reason) => MainThreadTasks.Reset(reason);
    internal static Task StartTaskBackground(Action callback, string nameID)
    {
        return StartTaskBackground(new Task(callback), nameID);
    }
    internal static Task StartTaskBackground(Task gameTask, string nameID)
    {
        Monitor.Log($"Try StartTask name: '{nameID}' on Android SModHook");
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
        return BackgroundTasks.Start(() =>
        {
            try
            {
                var startedAt = Stopwatch.GetTimestamp();
                Monitor.Log($"Starting Task On Background id: '{nameID}'");
                gameTask.RunSynchronously();
                var elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                Monitor.Log($"Completed Task On Background id: {nameID} in {elapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Monitor.Log($"Exception on task id: {nameID}");
                Monitor.Log($"{ex.GetLogSummary()}");
            }
        });
    }

}
