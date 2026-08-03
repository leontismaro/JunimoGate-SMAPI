using System;
using HarmonyLib;
using StardewModdingAPI.Framework;
using StardewModdingAPI.Internal;

namespace StardewModdingAPI.Mobile;

internal static class AndroidRuntimeBootstrap
{
    private static Harmony? harmony;

    internal static Harmony Harmony => harmony
        ?? throw new InvalidOperationException("Android runtime bootstrap has not completed.");

    public static void InitializeProcess()
    {
        AndroidLogger.Log("===========================");
        AndroidLogger.Log("===========================");
        AndroidLogger.Log("On AndroidRuntimeBootstrap.InitializeProcess()");

        try
        {
            Log.enabled = true;
            harmony = new Harmony(nameof(AndroidPatcher));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error on AndroidRuntimeBootstrap.InitializeProcess()");
            AndroidLogger.Log(ex);
            throw;
        }
    }

    public static void PrepareSession()
    {
        new SaveBackupZip().Start();
        AndroidPatcher.Apply(Harmony, SCore.Instance.SMAPIMonitor);
    }
}
