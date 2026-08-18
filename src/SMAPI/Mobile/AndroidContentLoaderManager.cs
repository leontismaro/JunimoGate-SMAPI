using System;
using System.Reflection;
using HarmonyLib;
using StardewModdingAPI.Framework;
using StardewModdingAPI.Mobile.Audio;
using StardewValley;
using StardewValley.Audio;
using StardewValley.Menus;

namespace StardewModdingAPI.Mobile;

internal static class AndroidContentLoaderManager
{
    private static readonly FieldInfo FinishedFirstInitSerializersField = RequireGameField("FinishedFirstInitSerializers");
    private static readonly FieldInfo FinishedFirstLoadContentField = RequireGameField("FinishedFirstLoadContent");
    private static readonly FieldInfo FinishedFirstInitSoundsField = RequireGameField("FinishedFirstInitSounds");
    private static readonly FieldInfo FinishedIncrementalLoadField = RequireGameField("FinishedIncrementalLoad");
    private static readonly MethodInfo AfterLoadContentMethod = AccessTools.Method(typeof(Game1), "AfterLoadContent")
        ?? throw new MissingMethodException(typeof(Game1).FullName, "AfterLoadContent");

    public static bool IsLoaded => LoadState == LoadStateEnum.Loaded;

    public static bool FinishedFirstInitSerializers
    {
        get => ReadBoolean(FinishedFirstInitSerializersField);
        set => FinishedFirstInitSerializersField.SetValue(null, value);
    }
    public static bool FinishedFirstLoadContent
    {
        get => ReadBoolean(FinishedFirstLoadContentField);
        set => FinishedFirstLoadContentField.SetValue(null, value);
    }

    public static bool FinishedFirstInitSounds
    {
        get => ReadBoolean(FinishedFirstInitSoundsField);
        set => FinishedFirstInitSoundsField.SetValue(null, value);
    }
    internal static void Reset()
    {
        LoadState = LoadStateEnum.None;
    }

    public static void UpdateMoveNextLoadContent()
    {
        if (LoadState == LoadStateEnum.None)
            OnSetupFirstTick();

        var currentLoaderEnumerator = SGame.LoadContentEnumerator
            ?? throw new InvalidOperationException("The game Content loader is unavailable.");
        bool isLoadContentFinish;
        using (AndroidRuntimeDiagnostics.Track("content-loader", "MoveNext"))
            isLoadContentFinish = currentLoaderEnumerator.MoveNext() is false;
        if (isLoadContentFinish)
            FinishedFirstLoadContent = true;

        if (FinishedFirstLoadContent && FinishedFirstInitSounds
            && FinishedFirstInitSerializers)
        {
            FinishedIncrementalLoadField.SetValue(null, true);
            LoadState = LoadStateEnum.Loaded;
            SGame.LoadContentEnumerator = null;
            OnPrefix_AfterLoadContent();
            using (AndroidRuntimeDiagnostics.Track("content-loader", "AfterLoadContent"))
                AfterLoadContentMethod.Invoke(Game1.game1, null);
            OnPostfix_AfterLoadContent();
        }
    }
    static void OnSetupFirstTick()
    {
        LoadState = LoadStateEnum.Loading;

        // Large Mod sets can block the Android game loop during their first update.
        // Use the game's own startup latch so that this session enters the title menu
        // directly instead of exposing a blank splash frame behind the loading overlay.
        TitleMenu.SkipSplashScreens = true;

        //change AudioCueModificationManager
        //debug
        Game1.CueModification = new CustomAudioCueModificationManager();
    }

    static IMonitor Monitor => SCore.Instance.SMAPIMonitor;
    static void Log(string msg) => Monitor.Log(msg);

    static void OnPrefix_AfterLoadContent()
    {
    }

    static void OnPostfix_AfterLoadContent()
    {
        if (SGame.game1 is not SGame game)
            throw new InvalidOperationException("The hosted SMAPI game is unavailable.");
        game.OnAndroidContentLoaded();
    }

    private static FieldInfo RequireGameField(string name) => AccessTools.Field(typeof(Game1), name)
        ?? throw new MissingFieldException(typeof(Game1).FullName, name);

    private static bool ReadBoolean(FieldInfo field) => field.GetValue(null) is bool value
        ? value
        : throw new InvalidOperationException($"The game field '{field.Name}' is unavailable.");


    public enum LoadStateEnum
    {
        None,
        Loading,
        Loaded,
    }
    public static LoadStateEnum LoadState = LoadStateEnum.None;
}
