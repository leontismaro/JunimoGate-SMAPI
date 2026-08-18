namespace StardewModdingAPI.Mobile;

/// <summary>Tracks consecutive Android game-loop failures and bounds repeated log detail.</summary>
internal sealed class AndroidUpdateFailureTracker
{
    private int count;

    internal FailureObservation RecordFailure()
    {
        int current = ++this.count;
        return new FailureObservation(
            current,
            ShouldLogDetails: current == 1,
            ShouldLogSuppressionNotice: current == 2);
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
        bool ShouldLogSuppressionNotice);
}
