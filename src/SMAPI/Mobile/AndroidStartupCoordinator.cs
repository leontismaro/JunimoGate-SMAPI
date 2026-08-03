using System;
using System.Threading;
using StardewValley;

namespace StardewModdingAPI.Mobile;

/// <summary>Coordinates the Android-only startup phases which must advance from the game thread.</summary>
internal sealed class AndroidStartupCoordinator
{
    private int phase = (int)StartupPhase.NotStarted;

    public StartupPhase Phase => (StartupPhase)Volatile.Read(ref this.phase);

    public void BeginModLoading()
    {
        AndroidContentLoaderManager.Reset();
        Volatile.Write(ref this.phase, (int)StartupPhase.ModLoading);
    }

    public void CompleteModLoading()
    {
        if (Interlocked.CompareExchange(
                ref this.phase,
                (int)StartupPhase.ContentLoading,
                (int)StartupPhase.ModLoading) != (int)StartupPhase.ModLoading)
        {
            throw new InvalidOperationException($"Mod loading completed from invalid Android startup phase '{this.Phase}'.");
        }
    }

    public void Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Volatile.Write(ref this.phase, (int)StartupPhase.Failed);
        AndroidSModHooks.CancelPendingMainThreadTasks(exception);
    }

    /// <returns>Whether normal SMAPI game updates can begin.</returns>
    public bool Update()
    {
        switch (this.Phase)
        {
            case StartupPhase.ModLoading:
                AndroidModLoaderManager.TickUpdate();
                return false;

            case StartupPhase.ContentLoading:
                AndroidContentLoaderManager.UpdateMoveNextLoadContent();
                if (AndroidContentLoaderManager.IsLoaded)
                    Volatile.Write(ref this.phase, (int)StartupPhase.WaitingForMenu);
                return false;

            case StartupPhase.WaitingForMenu:
                if (Game1.activeClickableMenu is null)
                    return false;
                AndroidModLoaderManager.StopLoggerToScreen();
                Volatile.Write(ref this.phase, (int)StartupPhase.Running);
                return true;

            case StartupPhase.Running:
                return true;

            case StartupPhase.Failed:
            case StartupPhase.NotStarted:
            default:
                return false;
        }
    }

    internal enum StartupPhase
    {
        NotStarted,
        ModLoading,
        ContentLoading,
        WaitingForMenu,
        Running,
        Failed,
    }
}
