using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using StardewModdingAPI.Framework;
using StardewModdingAPI.Internal;
using StardewModdingAPI.Mobile.Facade;
using StardewValley;
using StardewValley.Audio;
using StardewValley.Extensions;

namespace StardewModdingAPI.Mobile.Audio;

[HarmonyPatch]
internal class CustomAudioCueModificationManager : AudioCueModificationManager
{
    public enum LoadStage
    {
        None,
        Loading,
        Loaded,
    }
    public static CustomAudioCueModificationManager Instance { get; private set; }
    IMonitor monitor;
    public CustomAudioCueModificationManager()
    {
        Instance = this;
        this.monitor = SCore.Instance.SMAPIMonitor;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AudioCueModificationManager), nameof(OnStartup))]
    static bool Prefix_OnStartup()
    {
        Instance.MyStartUp();
        return false;
    }

    public LoadStage loadStage = LoadStage.None;

    void MyStartUp()
    {
        this.monitor.Log("Starting load cueModificationData...");
        this.cueModificationData = DataLoader.AudioChanges(Game1.content);
        this.ApplyAllCueModifications();
    }

    public override void ApplyAllCueModifications()
    {
        try
        {
            if (this.cueModificationData.Count == 0)
            {
                this.loadStage = LoadStage.Loaded;
                return;
            }

            this.cueLoadedDict.Clear();
            this.loadStage = LoadStage.Loading;
            AndroidModLoaderManager.StartLoggerToScreen();
            AndroidGameLoopManager.RegisterOnGameUpdating(this.OnGameUpdating);

            CueLoadPlan[] plans = this.cueModificationData
                .Select(pair => new CueLoadPlan(
                    pair.Key,
                    pair.Value.StreamedVorbis,
                    pair.Value.FilePaths?.Select(this.GetFilePath).ToArray() ?? []))
                .ToArray();
            AndroidSModHooks.StartTaskBackground(
                () => this.LoadCueModificationsInBackground(plans),
                "Decode Android audio cue modifications");

        }
        catch (Exception ex)
        {
            this.monitor.Log(ex.GetLogSummary(), LogLevel.Error);
        }
    }
    bool OnGameUpdating(GameTime time)
    {
        bool isLoaded = this.cueLoadedDict.Count == this.cueModificationData.Count;
        if (isLoaded)
        {
            this.loadStage = LoadStage.Loaded;
            AndroidGameLoopManager.UnregisterOnGameUpdating(this.OnGameUpdating);
            AndroidModLoaderManager.StopLoggerToScreen();

            return false;
        }

        return true;
    }

    private readonly ConcurrentDictionary<string, byte> cueLoadedDict = new();

    public override void ApplyCueModification(string key)
    {
        if (!this.cueModificationData.TryGetValue(key, out var modificationData))
            return;

        try
        {
            var effects = new List<SoundEffect>();
            foreach (string sourcePath in modificationData.FilePaths ?? [])
            {
                string filePath = this.GetFilePath(sourcePath);
                try
                {
                    effects.Add(this.LoadSoundSynchronously(filePath, modificationData.StreamedVorbis));
                }
                catch (Exception exception)
                {
                    this.LogSoundLoadError(filePath, exception);
                }
            }

            this.ApplyPreparedCueModification(key, effects.ToArray());
        }
        finally
        {
            this.MarkCueCompleted(key);
        }
    }

    private void LoadCueModificationsInBackground(CueLoadPlan[] plans)
    {
        foreach (CueLoadPlan plan in plans)
        {
            var effects = new List<SoundEffect>();
            try
            {
                foreach (string filePath in plan.FilePaths)
                {
                    try
                    {
                        SoundEffect effect;
                        bool vorbis = Path.GetExtension(filePath).EqualsIgnoreCase(".ogg");
                        if (vorbis && !plan.StreamedVorbis)
                        {
                            SoundEffectVorbis.DecodedSound decoded = SoundEffectVorbis.DecodeFromFilePath(filePath);
                            SoundEffect? published = null;
                            AndroidMainThread.InvokeOnMainThread(
                                () => published = SoundEffectVorbis.CreateFromDecoded(decoded),
                                $"Publish decoded audio: {Path.GetFileName(filePath)}");
                            effect = published ?? throw new InvalidOperationException("The decoded Android sound was not published.");
                        }
                        else
                        {
                            SoundEffect? published = null;
                            AndroidMainThread.InvokeOnMainThread(
                                () => published = this.LoadSoundSynchronously(filePath, plan.StreamedVorbis),
                                $"Load audio: {Path.GetFileName(filePath)}");
                            effect = published ?? throw new InvalidOperationException("The Android sound was not loaded.");
                        }

                        effects.Add(effect);
                    }
                    catch (Exception exception)
                    {
                        this.LogSoundLoadError(filePath, exception);
                    }
                }

                AndroidMainThread.InvokeOnMainThread(
                    () =>
                    {
                        this.ApplyPreparedCueModification(plan.Key, effects.ToArray());
                        this.MarkCueCompleted(plan.Key);
                    },
                    $"Apply audio cue: {plan.Key}");
            }
            catch (Exception exception)
            {
                this.monitor.Log($"Failed loading Android audio cue '{plan.Key}': {exception.GetLogSummary()}", LogLevel.Error);
            }
        }
    }

    private SoundEffect LoadSoundSynchronously(string filePath, bool streamedVorbis)
    {
        bool vorbis = Path.GetExtension(filePath).EqualsIgnoreCase(".ogg");
        if (vorbis && streamedVorbis)
            return OggStreamSoundEffect.CreateOggStreamFromFileName(filePath);
        if (vorbis)
            return SoundEffectVorbis.CreateFromFilePath(filePath);
        return SoundEffect.FromFile(filePath);
    }

    private void ApplyPreparedCueModification(string key, SoundEffect[] effects)
    {
        try
        {
            var cueModificationData = this.cueModificationData;
            if (!cueModificationData.TryGetValue(key, out var modification_data))
                return;

            bool is_modification = false;
            int category_index = Game1.audioEngine.IAudioEngine_GetCategoryIndex("Default");
            CueDefinition cue_definition;
            var soundBankWrapper = Game1.soundBank as SoundBankWrapper;
            var soundBank = soundBankWrapper.GetSoundBank();

            if (soundBank.Exists(modification_data.Id))
            {
                cue_definition = soundBank.GetCueDefinition(modification_data.Id);
                is_modification = true;
            }
            else
            {
                cue_definition = new CueDefinition();
                cue_definition.name = modification_data.Id;
            }
            if (modification_data.Category != null)
            {
                category_index = Game1.audioEngine.IAudioEngine_GetCategoryIndex(modification_data.Category);
            }
            if (modification_data.FilePaths != null)
            {
                cue_definition.SetSound(effects, category_index, modification_data.Looped, modification_data.UseReverb);
                if (is_modification)
                {
                    cue_definition.OnModified?.Invoke();
                }
            }
            soundBank.AddCue(cue_definition);
        }
        catch (NoAudioHardwareException)
        {
            Game1.log.Warn("Can't apply modifications for audio cue '" + key + "' because there's no audio hardware available.");
        }
        catch (Exception ex)
        {
            this.monitor.Log(ex.ToString(), LogLevel.Error);
        }
    }

    private void MarkCueCompleted(string key)
        => this.cueLoadedDict.TryAdd(key, 0);

    private void LogSoundLoadError(string filePath, Exception exception)
        => this.monitor.Log($"Error loading sound '{filePath}': {exception.GetLogSummary()}", LogLevel.Error);

    private sealed record CueLoadPlan(string Key, bool StreamedVorbis, string[] FilePaths);
}
