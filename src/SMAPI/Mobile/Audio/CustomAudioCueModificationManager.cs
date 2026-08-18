using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    internal bool IsReady => this.loadStage != LoadStage.Loading || Volatile.Read(ref this.pendingCueCount) == 0;
    private readonly Dictionary<string, CueIdentity> appliedCueIdentities = new(StringComparer.Ordinal);
    private int loadGeneration;
    private int pendingCueCount;
    private bool loadingCallbackActive;

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
            int generation = Interlocked.Increment(ref this.loadGeneration);
            Volatile.Write(ref this.pendingCueCount, this.cueModificationData.Count);
            if (this.cueModificationData.Count == 0)
            {
                this.loadStage = LoadStage.Loaded;
                return;
            }

            this.loadStage = LoadStage.Loading;
            if (!this.loadingCallbackActive)
            {
                this.loadingCallbackActive = true;
                AndroidModLoaderManager.StartLoggerToScreen();
                AndroidGameLoopManager.RegisterOnGameUpdating(this.OnGameUpdating);
            }

            var plans = new List<CueLoadPlan>();
            foreach ((string key, var modification) in this.cueModificationData)
            {
                try
                {
                    string filesIdentity = string.Join(
                        "\0",
                        (modification.FilePaths ?? [])
                            .Select(this.GetFilePath)
                            .Select(path =>
                            {
                                FileInfo info = new(path);
                                return info.Exists
                                    ? $"{path}\0{info.Length}\0{info.LastWriteTimeUtc.Ticks}"
                                    : $"{path}\0<missing>";
                            }));
                    CueIdentity identity = new(
                        modification.Id,
                        modification.Category,
                        modification.Looped,
                        modification.UseReverb,
                        modification.StreamedVorbis,
                        filesIdentity);
                    if (this.CanReuseCue(key, modification.Id, identity))
                    {
                        this.MarkCueCompleted(key, generation);
                        continue;
                    }

                    plans.Add(new CueLoadPlan(
                        key,
                        modification.StreamedVorbis,
                        modification.FilePaths?.Select(this.GetFilePath).ToArray() ?? [],
                        identity));
                }
                catch (Exception exception)
                {
                    this.monitor.Log($"Failed planning Android audio cue '{key}': {exception.GetLogSummary()}", LogLevel.Error);
                    this.MarkCueCompleted(key, generation);
                }
            }

            if (plans.Count == 0)
                return;

            try
            {
                AndroidSModHooks.StartTaskBackgroundNonBlocking(
                    () => this.LoadCueModificationsInBackground(plans.ToArray(), generation),
                    "Decode Android audio cue modifications");
            }
            catch
            {
                this.CompleteGeneration(generation);
                throw;
            }
        }
        catch (Exception ex)
        {
            this.monitor.Log(ex.GetLogSummary(), LogLevel.Error);
        }
    }
    bool OnGameUpdating(GameTime time)
    {
        if (Volatile.Read(ref this.pendingCueCount) == 0)
        {
            this.loadStage = LoadStage.Loaded;
            this.loadingCallbackActive = false;
            AndroidGameLoopManager.UnregisterOnGameUpdating(this.OnGameUpdating);
            AndroidModLoaderManager.StopLoggerToScreen();

            return false;
        }

        // Audio preparation is a dependency for save loading, but not for title
        // animation or input. Keep the completion callback for lifecycle cleanup
        // without turning the whole game update into a 40-second barrier.
        return false;
    }

    public override void ApplyCueModification(string key)
    {
        this.appliedCueIdentities.Remove(key);
        _ = this.TryApplyCueModification(key, out _);
    }

    private void LoadCueModificationsInBackground(CueLoadPlan[] plans, int generation)
    {
        try
        {
            foreach (CueLoadPlan plan in plans)
            {
                if (generation != Volatile.Read(ref this.loadGeneration))
                    return;

                var effects = new List<SoundEffect>();
                bool applied = false;
                try
                {
                    foreach (string filePath in plan.FilePaths)
                    {
                        try
                        {
                            if (Path.GetExtension(filePath).Equals(".ogg", StringComparison.OrdinalIgnoreCase)
                                && !plan.StreamedVorbis)
                            {
                                SoundEffectVorbis.DecodedSound decoded = SoundEffectVorbis.DecodeFromFilePath(filePath);
                                SoundEffect? published = null;
                                AndroidSModHooks.AddAudioTaskRunOnMainThreadDeferred(
                                    () => published = SoundEffectVorbis.CreateFromDecoded(decoded),
                                    $"Publish decoded audio: {Path.GetFileName(filePath)}")
                                    .GetAwaiter().GetResult();
                                effects.Add(published ?? throw new InvalidOperationException("Decoded audio was not published."));
                            }
                            else
                            {
                                SoundEffect? loaded = null;
                                AndroidSModHooks.AddAudioTaskRunOnMainThreadDeferred(
                                    () => loaded = this.LoadSoundSynchronously(filePath, plan.StreamedVorbis),
                                    $"Load audio: {Path.GetFileName(filePath)}")
                                    .GetAwaiter().GetResult();
                                effects.Add(loaded ?? throw new InvalidOperationException("Audio was not loaded."));
                            }
                        }
                        catch (Exception exception)
                        {
                            this.LogSoundLoadError(filePath, exception);
                        }
                    }

                    AndroidSModHooks.AddAudioTaskRunOnMainThreadDeferred(
                        () =>
                        {
                            if (generation != Volatile.Read(ref this.loadGeneration))
                            {
                                foreach (SoundEffect effect in effects)
                                    effect.Dispose();
                                return;
                            }
                            applied = this.TryApplyPreparedCueModification(plan.Key, effects.ToArray());
                            if (applied)
                                this.appliedCueIdentities[plan.Key] = plan.Identity;
                            else
                            {
                                foreach (SoundEffect effect in effects)
                                    effect.Dispose();
                            }
                        },
                        $"Apply audio cue: {plan.Key}")
                        .GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    this.monitor.Log($"Failed loading Android audio cue '{plan.Key}': {exception.GetLogSummary()}", LogLevel.Error);
                }
                finally
                {
                    this.MarkCueCompleted(plan.Key, generation);
                }
            }
        }
        finally
        {
            this.CompleteGeneration(generation);
        }
    }

    private SoundEffect LoadSoundSynchronously(string filePath, bool streamedVorbis)
    {
        bool vorbis = Path.GetExtension(filePath).Equals(".ogg", StringComparison.OrdinalIgnoreCase);
        if (vorbis && streamedVorbis)
            return OggStreamSoundEffect.CreateOggStreamFromFileName(filePath);
        if (vorbis)
            return SoundEffectVorbis.CreateFromFilePath(filePath);
        return SoundEffect.FromFile(filePath);
    }

    private bool TryApplyPreparedCueModification(string key, SoundEffect[] effects)
    {
        if (!this.cueModificationData.TryGetValue(key, out var modificationData))
            return false;

        try
        {
            bool isModification = false;
            int categoryIndex = Game1.audioEngine.IAudioEngine_GetCategoryIndex("Default");
            var soundBank = ((SoundBankWrapper)Game1.soundBank).GetSoundBank();
            CueDefinition cueDefinition;
            if (soundBank.Exists(modificationData.Id))
            {
                cueDefinition = soundBank.GetCueDefinition(modificationData.Id);
                isModification = true;
            }
            else
            {
                cueDefinition = new CueDefinition { name = modificationData.Id };
            }
            if (modificationData.Category is not null)
                categoryIndex = Game1.audioEngine.IAudioEngine_GetCategoryIndex(modificationData.Category);
            if (modificationData.FilePaths is not null)
            {
                cueDefinition.SetSound(effects, categoryIndex, modificationData.Looped, modificationData.UseReverb);
                if (isModification)
                    cueDefinition.OnModified?.Invoke();
            }
            soundBank.AddCue(cueDefinition);
            return true;
        }
        catch (NoAudioHardwareException)
        {
            Game1.log.Warn($"Can't apply modifications for audio cue '{key}' because there's no audio hardware available.");
            return false;
        }
        catch (Exception exception)
        {
            this.monitor.Log(exception.ToString(), LogLevel.Error);
            return false;
        }
    }

    private bool TryApplyCueModification(string key, out CueDefinition? appliedCue)
    {
        appliedCue = null;
        try
        {
            if (!this.cueModificationData.TryGetValue(key, out var modification_data))
                return false;

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
                SoundEffect[] effects = new SoundEffect[modification_data.FilePaths.Count];
                for (int i = 0; i < modification_data.FilePaths.Count; i++)
                {
                    string file_path = this.GetFilePath(modification_data.FilePaths[i]);
                    bool vorbis = Path.GetExtension(file_path).EqualsIgnoreCase(".ogg");
                    int invalid_sounds = 0;
                    try
                    {
                        SoundEffect sound_effect;
                        //.ogg file & streaming
                        if (vorbis && modification_data.StreamedVorbis)
                        {
                            sound_effect = OggStreamSoundEffect.CreateOggStreamFromFileName(file_path);
                        }
                        //.ogg file
                        else if (vorbis)
                        {
                            sound_effect = SoundEffectVorbis.CreateFromFilePath(file_path);
                        }
                        //general file such as .wav
                        else
                        {
                            sound_effect = SoundEffect.FromFile(file_path);
                        }

                        effects[i - invalid_sounds] = sound_effect;
                    }
                    catch (Exception e)
                    {
                        Game1.log.Error("Error loading sound: " + file_path, e);
                        invalid_sounds++;
                    }
                    if (invalid_sounds > 0)
                    {
                        Array.Resize(ref effects, effects.Length - invalid_sounds);
                    }
                }
                cue_definition.SetSound(effects, category_index, modification_data.Looped, modification_data.UseReverb);
                if (is_modification)
                {
                    cue_definition.OnModified?.Invoke();
                }
            }
            soundBank.AddCue(cue_definition);
            appliedCue = cue_definition;
            return true;
        }
        catch (NoAudioHardwareException)
        {
            Game1.log.Warn("Can't apply modifications for audio cue '" + key + "' because there's no audio hardware available.");
            return false;
        }
        catch (Exception ex)
        {
            this.monitor.Log(ex.ToString(), LogLevel.Error);
            return false;
        }
    }

    private bool CanReuseCue(string key, string cueId, CueIdentity identity)
    {
        if (!this.appliedCueIdentities.TryGetValue(key, out CueIdentity previous) || previous != identity)
            return false;

        if (Game1.soundBank is not SoundBankWrapper soundBankWrapper)
            return false;
        return soundBankWrapper.GetSoundBank().Exists(cueId);
    }

    private void LogSoundLoadError(string filePath, Exception exception)
        => this.monitor.Log($"Error loading sound '{filePath}': {exception.GetLogSummary()}", LogLevel.Error);

    private void MarkCueCompleted(string key, int generation)
    {
        if (generation != Volatile.Read(ref this.loadGeneration))
            return;

        if (Interlocked.Decrement(ref this.pendingCueCount) < 0)
            Volatile.Write(ref this.pendingCueCount, 0);
    }

    private void CompleteGeneration(int generation)
    {
        if (generation == Volatile.Read(ref this.loadGeneration))
            Volatile.Write(ref this.pendingCueCount, 0);
    }

    private sealed record CueLoadPlan(
        string Key,
        bool StreamedVorbis,
        string[] FilePaths,
        CueIdentity Identity);

    private sealed record CueIdentity(
        string Id,
        string? Category,
        bool Looped,
        bool UseReverb,
        bool StreamedVorbis,
        string Files);
}
