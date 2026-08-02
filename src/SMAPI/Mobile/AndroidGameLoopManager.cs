using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace StardewModdingAPI.Mobile;

// Keep the Android main-thread update callbacks used by SGameRunner and mobile services.
// This intentionally isn't a Harmony patch class; the public MonoGame provider owns frame timing.
internal static class AndroidGameLoopManager
{
    internal delegate bool OnGameUpdatingDelegate(GameTime gameTime);
    static HashSet<OnGameUpdatingDelegate> listOnGameUpdating = new();
    static Queue<OnGameUpdatingDelegate> queueOnGameUpdatingToAdd = new();
    static Queue<OnGameUpdatingDelegate> queueOnGameUpdatingToRemove = new();

    /// <summary>
    /// Register On Main Thread Only!!
    /// </summary>
    /// <param name="onGameUpdate"></param>
    internal static void RegisterOnGameUpdating(OnGameUpdatingDelegate onGameUpdate)
    {
        queueOnGameUpdatingToAdd.Enqueue(onGameUpdate);
    }

    /// <summary>
    /// Unregister On Main Thread Only!!
    /// </summary>
    /// <param name="onGameUpdate"></param>
    internal static void UnregisterOnGameUpdating(OnGameUpdatingDelegate onGameUpdate)
    {
        queueOnGameUpdatingToRemove.Enqueue(onGameUpdate);
    }

    public static bool IsSkipOriginalGameUpdating { get; private set; } = false;
    internal static void UpdateFrame_OnGameUpdating(GameTime gameTime)
    {
        //reset
        IsSkipOriginalGameUpdating = false;

        if (queueOnGameUpdatingToAdd.Count > 0)
        {
            while (queueOnGameUpdatingToAdd.TryDequeue(out OnGameUpdatingDelegate? item))
            {
                if (item is not null)
                    listOnGameUpdating.Add(item);
            }
        }

        if (queueOnGameUpdatingToRemove.Count > 0)
        {
            while (queueOnGameUpdatingToRemove.TryDequeue(out OnGameUpdatingDelegate? item))
            {
                if (item is not null)
                    listOnGameUpdating.Remove(item);
            }
        }

        //Console.WriteLine("Android Looper OnGameUpdating...");
        foreach (var callback in listOnGameUpdating)
        {
            if (callback(gameTime))
            {
                IsSkipOriginalGameUpdating = true;
            }
        }
    }

}
