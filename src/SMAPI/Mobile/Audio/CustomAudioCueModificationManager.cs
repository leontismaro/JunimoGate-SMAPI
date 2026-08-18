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

    internal bool IsReady => Volatile.Read(ref this.preparation)?.Snapshot.IsReady != false;
    private readonly Dictionary<string, CueIdentity> appliedCueIdentities = new(StringComparer.Ordinal);
    private int nextGeneration;
    private AndroidAudioPreparationState? preparation;
    private readonly SemaphoreSlim generationWorkerGate = new(1, 1);
    private IDisposable? loadingLoggerLease;
    private IDisposable? updateRegistration;

    void MyStartUp()
    {
        this.monitor.Log("Starting load cueModificationData...");
        this.cueModificationData = DataLoader.AudioChanges(Game1.content);
        this.ApplyAllCueModifications();
    }

    public override void ApplyAllCueModifications()
    {
        AndroidAudioPreparationState? generation = null;
        try
        {
            generation = new AndroidAudioPreparationState(
                Interlocked.Increment(ref this.nextGeneration),
                this.cueModificationData.Count);
            Interlocked.Exchange(ref this.preparation, generation)?.Supersede();
            if (this.cueModificationData.Count == 0)
                return;

            this.loadingLoggerLease ??= AndroidModLoaderManager.AcquireLoggerToScreen();
            this.updateRegistration ??= AndroidGameLoopManager.RegisterOnGameUpdating(this.OnGameUpdating);

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
                        generation.CompleteCue(hadError: false);
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
                    generation.CompleteCue(hadError: true);
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
                generation.CompleteRemainingWithErrors();
                throw;
            }
        }
        catch (Exception ex)
        {
            generation?.CompleteRemainingWithErrors();
            this.monitor.Log(ex.GetLogSummary(), LogLevel.Error);
        }
    }
    bool OnGameUpdating(GameTime time)
    {
        AndroidAudioPreparationState? current = Volatile.Read(ref this.preparation);
        if (current is null || current.Snapshot.Status != AndroidAudioPreparationStatus.Preparing)
        {
            Interlocked.Exchange(ref this.updateRegistration, null)?.Dispose();
            Interlocked.Exchange(ref this.loadingLoggerLease, null)?.Dispose();

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

    private void LoadCueModificationsInBackground(
        CueLoadPlan[] plans,
        AndroidAudioPreparationState generation)
    {
        bool gateEntered = false;
        try
        {
            generation.CancellationToken.ThrowIfCancellationRequested();
            this.generationWorkerGate.Wait(generation.CancellationToken);
            gateEntered = true;

            if (!this.IsCurrentGeneration(generation))
                return;

            foreach (CueLoadPlan plan in plans)
            {
                if (!this.IsCurrentGeneration(generation))
                    return;

                var effects = new List<SoundEffect>();
                bool ownershipTransferred = false;
                bool hadError = false;
                try
                {
                    foreach (string filePath in plan.FilePaths)
                    {
                        if (!this.IsCurrentGeneration(generation))
                            return;

                        try
                        {
                            if (Path.GetExtension(filePath).Equals(".ogg", StringComparison.OrdinalIgnoreCase)
                                && !plan.StreamedVorbis)
                            {
                                SoundEffectVorbis.DecodedSound decoded = SoundEffectVorbis.DecodeFromFilePath(filePath);
                                if (!this.IsCurrentGeneration(generation))
                                    return;

                                SoundEffect? published = null;
                                AndroidSModHooks.AddAudioTaskRunOnMainThreadDeferred(
                                    () =>
                                    {
                                        if (this.IsCurrentGeneration(generation))
                                            published = SoundEffectVorbis.CreateFromDecoded(decoded);
                                    },
                                    $"Publish decoded audio: {Path.GetFileName(filePath)}")
                                    .GetAwaiter().GetResult();

                                if (!this.IsCurrentGeneration(generation))
                                {
                                    published?.Dispose();
                                    return;
                                }
                                effects.Add(published ?? throw new InvalidOperationException("Decoded audio was not published."));
                            }
                            else
                            {
                                SoundEffect? loaded = null;
                                AndroidSModHooks.AddAudioTaskRunOnMainThreadDeferred(
                                    () =>
                                    {
                                        if (this.IsCurrentGeneration(generation))
                                            loaded = this.LoadSoundSynchronously(filePath, plan.StreamedVorbis);
                                    },
                                    $"Load audio: {Path.GetFileName(filePath)}")
                                    .GetAwaiter().GetResult();

                                if (!this.IsCurrentGeneration(generation))
                                {
                                    loaded?.Dispose();
                                    return;
                                }
                                effects.Add(loaded ?? throw new InvalidOperationException("Audio was not loaded."));
                            }
                        }
                        catch (OperationCanceledException) when (generation.CancellationToken.IsCancellationRequested)
                        {
                            return;
                        }
                        catch (Exception exception)
                        {
                            hadError = true;
                            this.LogSoundLoadError(filePath, exception);
                        }
                    }

                    if (!this.IsCurrentGeneration(generation))
                        return;

                    AndroidSModHooks.AddAudioTaskRunOnMainThreadDeferred(
                        () =>
                        {
                            if (!this.IsCurrentGeneration(generation))
                                return;

                            bool applied = this.TryApplyPreparedCueModification(plan.Key, effects.ToArray());
                            hadError |= !applied;
                            if (applied)
                            {
                                ownershipTransferred = true;
                                this.appliedCueIdentities[plan.Key] = plan.Identity;
                            }
                        },
                        $"Apply audio cue: {plan.Key}")
                        .GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (generation.CancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    hadError = true;
                    this.monitor.Log($"Failed loading Android audio cue '{plan.Key}': {exception.GetLogSummary()}", LogLevel.Error);
                }
                finally
                {
                    if (!ownershipTransferred)
                    {
                        foreach (SoundEffect effect in effects)
                            effect.Dispose();
                    }
                    generation.CompleteCue(hadError);
                }
            }
        }
        catch (OperationCanceledException) when (generation.CancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (gateEntered)
                this.generationWorkerGate.Release();
            generation.CompleteRemainingWithErrors();
        }
    }

    private bool IsCurrentGeneration(AndroidAudioPreparationState generation)
        => ReferenceEquals(generation, Volatile.Read(ref this.preparation))
            && !generation.CancellationToken.IsCancellationRequested;

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
