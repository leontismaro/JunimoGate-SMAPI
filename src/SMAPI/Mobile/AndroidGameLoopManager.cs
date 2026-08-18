using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Xna.Framework;

namespace StardewModdingAPI.Mobile;

// Keep the Android main-thread update callbacks used by SGameRunner and mobile services.
// This intentionally isn't a Harmony patch class; the public MonoGame provider owns frame timing.
internal static class AndroidGameLoopManager
{
    internal delegate bool OnGameUpdatingDelegate(GameTime gameTime);
    private static readonly Dictionary<long, OnGameUpdatingDelegate> ActiveCallbacks = new();
    private static readonly Queue<CallbackRegistration> PendingAdditions = new();
    private static readonly Queue<long> PendingRemovals = new();
    private static long nextRegistrationId;

    /// <summary>
    /// Register On Main Thread Only!!
    /// </summary>
    /// <param name="onGameUpdate"></param>
    internal static IDisposable RegisterOnGameUpdating(OnGameUpdatingDelegate onGameUpdate)
    {
        ArgumentNullException.ThrowIfNull(onGameUpdate);
        var registration = new CallbackRegistration(
            Interlocked.Increment(ref nextRegistrationId),
            onGameUpdate);
        PendingAdditions.Enqueue(registration);
        return registration;
    }

    public static bool IsSkipOriginalGameUpdating { get; private set; } = false;
    internal static void UpdateFrame_OnGameUpdating(GameTime gameTime)
    {
        //reset
        IsSkipOriginalGameUpdating = false;

        if (PendingAdditions.Count > 0)
        {
            while (PendingAdditions.TryDequeue(out CallbackRegistration? registration))
                ActiveCallbacks[registration.Id] = registration.Callback;
        }

        if (PendingRemovals.Count > 0)
        {
            while (PendingRemovals.TryDequeue(out long registrationId))
                ActiveCallbacks.Remove(registrationId);
        }

        //Console.WriteLine("Android Looper OnGameUpdating...");
        foreach (var callback in ActiveCallbacks.Values)
        {
            if (callback(gameTime))
            {
                IsSkipOriginalGameUpdating = true;
            }
        }
    }

    private sealed class CallbackRegistration : IDisposable
    {
        private int disposed;

        internal CallbackRegistration(long id, OnGameUpdatingDelegate callback)
        {
            this.Id = id;
            this.Callback = callback;
        }

        internal long Id { get; }
        internal OnGameUpdatingDelegate Callback { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) == 0)
                PendingRemovals.Enqueue(this.Id);
        }
    }

}
