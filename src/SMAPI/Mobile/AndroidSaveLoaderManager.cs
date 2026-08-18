using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI.Enums;
using StardewModdingAPI.Framework;
using StardewModdingAPI.Mobile.Audio;
using StardewValley;

namespace StardewModdingAPI.Mobile;

internal static class AndroidSaveLoaderManager
{
    private static readonly AndroidWorldEntryGate WorldEntryGate = new();
    public static bool IsSaveParsed => Context.LoadStage is LoadStage.SaveParsed;
    static Monitor monitor = null!;
    private static bool isLoaderActive;
    private static bool isWaitingForAudio;

    internal static void Init()
    {
        WorldEntryGate.Reset();
        isLoaderActive = false;
        isWaitingForAudio = false;
        AndroidGameLoopManager.RegisterOnGameUpdating(OnGameUpdating);
    }

    internal static void OnCreatedNewCharacter()
        => WorldEntryGate.Request();

    internal static void StartLoader()
    {
        if (isLoaderActive)
            return;

        monitor = SCore.Instance.SMAPIMonitor as Monitor
            ?? throw new InvalidOperationException("The Android save loader requires the SMAPI monitor.");
        monitor.Log("Game loader with AndroidSaveLoader currentLoader.MoveNext()", Monitor.ContextLogLevel);
        isLoaderActive = true;
    }

    private static bool OnGameUpdating(GameTime gameTime)
    {
        if (Game1.currentLoader is not null)
            WorldEntryGate.Request();

        bool isAudioReady = CustomAudioCueModificationManager.Instance is not { IsReady: false };
        bool shouldBlock = WorldEntryGate.ShouldBlock(isAudioReady);
        if (shouldBlock)
        {
            if (!isWaitingForAudio)
            {
                isWaitingForAudio = true;
                SCore.Instance.SMAPIMonitor.Log(
                    "Game world entry waiting for Android audio cues.",
                    Monitor.ContextLogLevel);
            }
            return true;
        }

        if (isWaitingForAudio)
        {
            isWaitingForAudio = false;
            SCore.Instance.SMAPIMonitor.Log(
                "Android audio cues ready; resuming game world entry.",
                Monitor.ContextLogLevel);
        }

        return isLoaderActive && UpdateSaveLoader(gameTime);
    }

    //run Save.currentLoader.NextMove() within main game updating
    private static bool UpdateSaveLoader(GameTime gameTime)
    {
        using (AndroidRuntimeDiagnostics.Track("save-loader", "UpdateTitleScreenDuringLoadingMode"))
            Game1.game1.UpdateTitleScreenDuringLoadingMode();
        var score = SCore.Instance;

        // raise load stage changed
        int? step = Game1.currentLoader?.Current;
        switch (step)
        {
            case 20 when (!IsSaveParsed && SaveGame.loaded != null):
                score.OnLoadStageChanged(LoadStage.SaveParsed);
                break;

            case 36:
                score.OnLoadStageChanged(LoadStage.SaveLoadedBasicInfo);
                break;

            case 50:
                score.OnLoadStageChanged(LoadStage.SaveLoadedLocations);
                break;

            default:
                if (Game1.gameMode == Game1.playingGameMode)
                    score.OnLoadStageChanged(LoadStage.Preloaded);
                break;
        }

        if (step is null)
        {
            //save game loaded
            //break; // done
            monitor.Log("Game loader done.", Monitor.ContextLogLevel);
            isLoaderActive = false;
            return false;
        }
        else
        {
            return true;
        }
    }
}
