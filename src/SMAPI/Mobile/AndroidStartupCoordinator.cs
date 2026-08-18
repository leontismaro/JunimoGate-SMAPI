using System;
using System.Threading;
using StardewModdingAPI.AndroidHost;
using StardewValley;

namespace StardewModdingAPI.Mobile;

/// <summary>Coordinates the Android-only startup phases which must advance from the game thread.</summary>
internal sealed class AndroidStartupCoordinator
{
    private int phase = (int)StartupPhase.NotStarted;
    private IDisposable? loadingLoggerLease;

    public StartupPhase Phase => (StartupPhase)Volatile.Read(ref this.phase);

    public void BeginModLoading()
    {
        AndroidContentLoaderManager.Reset();
        this.loadingLoggerLease?.Dispose();
        this.loadingLoggerLease = AndroidModLoaderManager.AcquireLoggerToScreen();
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

    public bool Fail(Exception exception, string failureCode = "android_startup_failed")
    {
        ArgumentNullException.ThrowIfNull(exception);
        while (true)
        {
            StartupPhase current = this.Phase;
            if (current is StartupPhase.Failed or StartupPhase.Running)
                return false;
            if (Interlocked.CompareExchange(
                    ref this.phase,
                    (int)StartupPhase.Failed,
                    (int)current) == (int)current)
            {
                Interlocked.Exchange(ref this.loadingLoggerLease, null)?.Dispose();
                AndroidHostServices.ReportFailure(
                    new SmapiFailure(failureCode, exception.Message, exception));
                return true;
            }
        }
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
                Interlocked.Exchange(ref this.loadingLoggerLease, null)?.Dispose();
                if (Interlocked.CompareExchange(
                        ref this.phase,
                        (int)StartupPhase.Running,
                        (int)StartupPhase.WaitingForMenu) != (int)StartupPhase.WaitingForMenu)
                    return false;
                AndroidHostServices.ReportRunning();
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
