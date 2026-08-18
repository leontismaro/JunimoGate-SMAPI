using System.Globalization;
using System.Threading;

namespace StardewModdingAPI.AndroidHost;

public static class AndroidCulturePolicy
{
    /// <summary>Apply the invariant data culture expected by the game and SMAPI without changing the UI language.</summary>
    public static void ApplyInvariantDataCulture()
    {
        // MonoGame runs Android updates on threads created after the host entry point.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
    }
}
