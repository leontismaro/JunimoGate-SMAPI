using System;
using System.Collections.Concurrent;
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

    internal static void Init()
    {
        AndroidGameLoopManager.RegisterOnGameUpdating(OnGameUpdating_TaskUpdate);
    }

    internal static bool OnGameUpdating_TaskUpdate(GameTime time)
    {

#if false
        //debug only
        if (SCore.ProcessTicksElapsed % 30 == 0 && queueTaskNeedToStartOnMainThread.IsEmpty is false)
        {
            Console.WriteLine();
            Console.WriteLine("SMod Hook Updating..");
            Console.WriteLine("task: " + queueTaskNeedToStartOnMainThread.Count);
            //foreach (var task in tasks)
            //{
            //    Console.WriteLine($"status task ID: {task.Id}");
            //    Console.WriteLine($"  status: {task.Status}");
            //    Console.WriteLine($"  IsCanceled: {task.IsCanceled}");
            //    Console.WriteLine($"  IsCompleted: {task.IsCompleted}");
            //    Console.WriteLine($"  IsCompletedSuccessfully: {task.IsCompletedSuccessfully}");
            //    Console.WriteLine($"  IsFaulted: {task.IsFaulted}");
            //}
        }
#endif

        bool markSkipGameUpdating = false;

        //process task on main thread
        //update main thread task

        // millisecond 1000.0 == 1 sec
        double runTaskOnMainThreadTotalTime = 0;
        while (queueTaskNeedToStartOnMainThread.TryDequeue(out var task))
        {
            bool shouldShowLogTask = task.name is not null;
            markSkipGameUpdating = true;
            var startedAt = Stopwatch.GetTimestamp();
            //if (shouldShowLogTask)
            //    Monitor.Log($"Start taskOnMainThread: '{task.name}'");

            task.task.RunSynchronously();
            var elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            runTaskOnMainThreadTotalTime += elapsedMilliseconds;
            if (shouldShowLogTask)
            {
                Monitor.Log($"Done taskOnMainThread: '{task.name}' in {elapsedMilliseconds}ms");
            }

            //debug
            if (runTaskOnMainThreadTotalTime > 2000)
            {
                Monitor.Log($"Warn!!, current task MainThread '{task.name}' " +
                    $"it's very long time in {runTaskOnMainThreadTotalTime:F3}ms", LogLevel.Warn);
            }

            //limit run task
            //maybe 1-2 frame, or 16ms or 32ms
            if (runTaskOnMainThreadTotalTime > 32)
            {
                break;
            }
        }

        if (BackgroundTasks.HasPending)
            markSkipGameUpdating = true;

        return markSkipGameUpdating;
    }
    internal class TaskOnMainThread
    {
        public readonly string? name;
        public readonly Task task;
        public TaskOnMainThread(Task task, string? name)
        {
            this.task = task;
            this.name = name;
        }
    }
    static ConcurrentQueue<TaskOnMainThread> queueTaskNeedToStartOnMainThread = new();

    internal static Task AddTaskRunOnMainThread(Action callback, string name)
        => AddTaskRunOnMainThread(new Task(callback), name);

    internal static Task AddTaskRunOnMainThread(Task yourTask, string? taskName)
    {
        var taskOnMainThread = new TaskOnMainThread(yourTask, taskName);
        queueTaskNeedToStartOnMainThread.Enqueue(taskOnMainThread);
        return yourTask;
    }
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
