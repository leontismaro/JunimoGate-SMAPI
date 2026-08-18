using System;
using System.Threading;

namespace StardewModdingAPI.Mobile.Audio;

internal enum AndroidAudioPreparationStatus
{
    Preparing,
    Ready,
    CompletedWithErrors,
    Superseded,
}

internal sealed class AndroidAudioPreparationState
{
    private readonly object syncRoot = new();
    private readonly CancellationTokenSource cancellation = new();
    private AndroidAudioPreparationStatus status;
    private int remainingCueCount;
    private int errorCount;

    public AndroidAudioPreparationState(int generation, int cueCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(generation);
        ArgumentOutOfRangeException.ThrowIfNegative(cueCount);
        this.Generation = generation;
        this.remainingCueCount = cueCount;
        this.status = cueCount == 0
            ? AndroidAudioPreparationStatus.Ready
            : AndroidAudioPreparationStatus.Preparing;
    }

    public int Generation { get; }

    public CancellationToken CancellationToken => this.cancellation.Token;

    public AndroidAudioPreparationSnapshot Snapshot
    {
        get
        {
            lock (this.syncRoot)
                return new AndroidAudioPreparationSnapshot(this.status, this.remainingCueCount, this.errorCount);
        }
    }

    public void CompleteCue(bool hadError)
    {
        lock (this.syncRoot)
        {
            if (this.status != AndroidAudioPreparationStatus.Preparing)
                return;
            if (hadError)
                this.errorCount++;
            if (this.remainingCueCount > 0)
                this.remainingCueCount--;
            if (this.remainingCueCount == 0)
                this.status = this.errorCount == 0
                    ? AndroidAudioPreparationStatus.Ready
                    : AndroidAudioPreparationStatus.CompletedWithErrors;
        }
    }

    public void CompleteRemainingWithErrors()
    {
        lock (this.syncRoot)
        {
            if (this.status != AndroidAudioPreparationStatus.Preparing)
                return;
            this.errorCount += this.remainingCueCount;
            this.remainingCueCount = 0;
            this.status = AndroidAudioPreparationStatus.CompletedWithErrors;
        }
    }

    public void Supersede()
    {
        bool cancel;
        lock (this.syncRoot)
        {
            cancel = this.status == AndroidAudioPreparationStatus.Preparing;
            if (cancel)
                this.status = AndroidAudioPreparationStatus.Superseded;
        }
        if (cancel)
            this.cancellation.Cancel();
    }
}

internal readonly record struct AndroidAudioPreparationSnapshot(
    AndroidAudioPreparationStatus Status,
    int RemainingCueCount,
    int ErrorCount)
{
    public bool IsReady => this.Status != AndroidAudioPreparationStatus.Preparing;
}
