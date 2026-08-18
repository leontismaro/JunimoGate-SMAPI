#if SMAPI_FOR_ANDROID
namespace StardewModdingAPI.Framework.Input;

/// <summary>Tracks whether the Android input devices have been sampled for the current game frame.</summary>
internal sealed class AndroidInputSnapshotGate
{
    private bool IsSnapshotReady;

    /// <summary>Start a new game frame.</summary>
    public void BeginFrame()
    {
        this.IsSnapshotReady = false;
    }

    /// <summary>Get whether the caller should sample the physical input devices.</summary>
    public bool TryBeginSampling()
    {
        if (this.IsSnapshotReady)
            return false;

        this.IsSnapshotReady = true;
        return true;
    }
}
#endif
