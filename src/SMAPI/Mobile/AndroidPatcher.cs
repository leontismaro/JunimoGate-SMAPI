using System;
using HarmonyLib;
using StardewModdingAPI.Framework;
using StardewModdingAPI.Internal;
using StardewModdingAPI.Mobile.Facade;
using StardewModdingAPI.Mobile.Vectors;
using StardewValley.Menus;

namespace StardewModdingAPI.Mobile;

[HarmonyPatch]
internal static class AndroidPatcher
{
    internal static void Apply(Harmony harmony, IMonitor monitor)
    {
        monitor.Log("On ApplyHarmonyPatchAll()..");
        try
        {
            harmony.PatchAll();
            monitor.Log("Done harmony.PatchAll()");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            monitor.Log(ex.GetLogSummary(), LogLevel.Error);
            throw;
        }

        monitor.Log("Applying Android vector converters...");
        VectorTypeConverterFix.ApplyPatch(harmony);
        // MobileFarmChooser's optional farm-selector compatibility detours target methods that
        // Android Mono may not JIT-compile before a menu instance exists. They are not needed for
        // the SMAPI title-screen/runtime baseline; defer this optional patch family until the
        // menu is created by a future lifecycle-aware adapter.
        monitor.Log("Applying Android letter viewer adapters...");
        LetterViewerMenuRewriter.ApplyPatch(harmony);
        monitor.Log("Android compatibility adapters ready.");
    }

    // Disable checkForAndLoadEmergencySave for Emergency Save
    [HarmonyPatch(typeof(TitleMenu), nameof(TitleMenu.checkForAndLoadEmergencySave))]
    [HarmonyPrefix]
    static bool Disable_checkForAndLoadEmergencySave(ref bool __result)
    {
        TitleMenu.PromptedEmergencySave = true;
        __result = false;
        return false;
    }
}
