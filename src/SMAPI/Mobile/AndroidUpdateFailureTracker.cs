using System;

namespace StardewModdingAPI.Mobile;

/// <summary>Tracks consecutive Android game-loop failures and bounds repeated log detail.</summary>
internal sealed class AndroidUpdateFailureTracker
{
    private const int DefaultMaxRecoverableFailures = 60;
    private readonly int maxRecoverableFailures;
    private int count;

    internal AndroidUpdateFailureTracker(int maxRecoverableFailures = DefaultMaxRecoverableFailures)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRecoverableFailures);
        this.maxRecoverableFailures = maxRecoverableFailures;
    }

    internal FailureObservation RecordFailure()
    {
        int current = ++this.count;
        return new FailureObservation(
            current,
            ShouldLogDetails: current == 1,
            ShouldLogSuppressionNotice: current == 2,
            ShouldTerminate: current > this.maxRecoverableFailures);
    }

    internal int Reset()
    {
        int previous = this.count;
        this.count = 0;
        return previous;
    }

    internal readonly record struct FailureObservation(
        int ConsecutiveFailures,
        bool ShouldLogDetails,
        bool ShouldLogSuppressionNotice,
        bool ShouldTerminate);
}
