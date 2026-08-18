using System.Threading;

namespace StardewModdingAPI.Mobile;

/// <summary>Blocks entry into a game world until Android startup dependencies are ready.</summary>
internal sealed class AndroidWorldEntryGate
{
    private int requested;

    public bool IsRequested => Volatile.Read(ref this.requested) != 0;

    public void Request()
        => Volatile.Write(ref this.requested, 1);

    public bool ShouldBlock(bool isReady)
    {
        if (!this.IsRequested)
            return false;
        if (!isReady)
            return true;

        Volatile.Write(ref this.requested, 0);
        return false;
    }

    public void Reset()
        => Volatile.Write(ref this.requested, 0);
}
