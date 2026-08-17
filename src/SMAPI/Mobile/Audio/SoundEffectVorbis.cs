using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Microsoft.Xna.Framework.Audio;
using NVorbis;
using NVorbis.Contracts;

namespace StardewModdingAPI.Mobile.Facade;

public class SoundEffectVorbis : SoundEffect
{
    public SoundEffectVorbis(byte[] buffer, int sampleRate, AudioChannels channels) : base(buffer, sampleRate, channels)
    {
    }

    public SoundEffectVorbis(byte[] buffer, int offset, int count, int sampleRate, AudioChannels channels, int loopStart, int loopLength) : base(buffer, offset, count, sampleRate, channels, loopStart, loopLength)
    {
    }

    static readonly FieldInfo _duration_FI = AccessTools.Field(typeof(SoundEffect), "_duration");
    static readonly MethodInfo Initialize_MI = AccessTools.Method(typeof(SoundEffect), "Initialize");
    static readonly MethodInfo PlatformInitializePcm_MI = AccessTools.Method(typeof(SoundEffect), "PlatformInitializePcm");

    public static SoundEffectVorbis CreateFromFilePath(string soundFilePath)
    {
        Console.WriteLine("starting load sound vorbis: " + soundFilePath);

        var st = Stopwatch.StartNew();
        using FileStream stream = new FileStream(soundFilePath, FileMode.Open);
        using (VorbisReader vorbis_reader = new VorbisReader(stream, closeOnDispose: true))
        {
            const int bytes_per_sample = 2;
            int sampleCount = checked((int)(vorbis_reader.TotalSamples * vorbis_reader.Channels));
            int chunkSize = Math.Min(sampleCount, 16 * 1024);
            float[] float_buffer = new float[chunkSize];
            short[] cast_buffer = new short[float_buffer.Length];
            byte[] xna_buffer = new byte[checked(sampleCount * bytes_per_sample)];
            int read_samples = 0;
            while (read_samples < sampleCount)
            {
                int currentCount = vorbis_reader.ReadSamples(
                    float_buffer,
                    0,
                    Math.Min(float_buffer.Length, sampleCount - read_samples));
                if (currentCount == 0)
                    break;

                OggStream.CastBuffer(float_buffer, cast_buffer, currentCount);
                Buffer.BlockCopy(
                    cast_buffer,
                    0,
                    xna_buffer,
                    checked(read_samples * bytes_per_sample),
                    checked(currentCount * bytes_per_sample));
                read_samples += currentCount;
            }

            if (read_samples != sampleCount)
                Array.Resize(ref xna_buffer, checked(read_samples * bytes_per_sample));

            var soundEffect = AccessTools.CreateInstance<SoundEffectVorbis>();
            Initialize_MI.Invoke(soundEffect, null);
            _duration_FI.SetValue(soundEffect, vorbis_reader.TotalTime);
            PlatformInitializePcm_MI.Invoke(soundEffect, [
                xna_buffer, 0, xna_buffer.Length, 16,
                vorbis_reader.SampleRate,
                (AudioChannels)vorbis_reader.Channels, 0,
                read_samples / vorbis_reader.Channels
            ]);

            Console.WriteLine($"created SoundEffect: {soundEffect} in {st.Elapsed.TotalSeconds}s");
            return soundEffect;
        }
    }
}
