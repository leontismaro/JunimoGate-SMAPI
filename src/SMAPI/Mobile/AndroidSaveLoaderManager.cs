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

    internal static void Init()
    {
        WorldEntryGate.Reset();
        AndroidGameLoopManager.RegisterOnGameUpdating(OnGameUpdating);
    }

    internal static void OnCreatedNewCharacter()
        => WorldEntryGate.Request();

    internal static void StartLoader()
    {
        if (!WorldEntryGate.BeginLoading())
            return;

        monitor = SCore.Instance.SMAPIMonitor as Monitor
            ?? throw new InvalidOperationException("The Android save loader requires the SMAPI monitor.");
        monitor.Log("Game loader with AndroidSaveLoader currentLoader.MoveNext()", Monitor.ContextLogLevel);
    }

    private static bool OnGameUpdating(GameTime gameTime)
    {
        if (Game1.currentLoader is not null)
            WorldEntryGate.Request();

        bool isAudioReady = CustomAudioCueModificationManager.Instance is not { IsReady: false };
        AndroidWorldEntryGate.WorldEntryObservation observation = WorldEntryGate.ObserveDependency(isAudioReady);
        if (observation.Transition == AndroidWorldEntryGate.WorldEntryTransition.StartedWaiting)
        {
            SCore.Instance.SMAPIMonitor.Log(
                "Game world entry waiting for Android audio cues.",
                Monitor.ContextLogLevel);
        }
        else if (observation.Transition == AndroidWorldEntryGate.WorldEntryTransition.DependencyReady)
        {
            SCore.Instance.SMAPIMonitor.Log(
                "Android audio cues ready; resuming game world entry.",
                Monitor.ContextLogLevel);
        }
        if (observation.ShouldBlock)
            return true;

        if (!observation.ShouldAdvanceLoader)
            return false;
        bool isStillLoading = UpdateSaveLoader(gameTime);
        if (!isStillLoading)
            WorldEntryGate.CompleteLoading();
        return isStillLoading;
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
            return false;
        }
        else
        {
            return true;
        }
    }
}
