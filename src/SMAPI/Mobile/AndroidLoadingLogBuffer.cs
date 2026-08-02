using System;
using StardewModdingAPI.Internal.ConsoleWriting;

namespace StardewModdingAPI.Mobile;

internal readonly record struct AndroidLoadingLogLine(ConsoleLogLevel Level, string Text);

/// <summary>Keeps a bounded window of structured log lines for the temporary Android loading screen.</summary>
internal sealed class AndroidLoadingLogBuffer
{
    private readonly object sync = new();
    private readonly AndroidLoadingLogLine[] lines;
    private int next;
    private int count;

    public AndroidLoadingLogBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        lines = new AndroidLoadingLogLine[capacity];
    }

    internal int Count
    {
        get
        {
            lock (sync)
                return count;
        }
    }

    public void Append(ConsoleLogLevel level, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (sync)
        {
            var start = 0;
            for (var index = 0; index < message.Length; index++)
            {
                if (message[index] is not ('\r' or '\n'))
                    continue;

                AppendLine(level, message[start..index]);
                if (message[index] == '\r' && index + 1 < message.Length && message[index + 1] == '\n')
                    index++;
                start = index + 1;
            }
            AppendLine(level, message[start..]);
        }
    }

    public AndroidLoadingLogLine[] SnapshotNewestFirst(int maximumCount)
    {
        if (maximumCount < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        lock (sync)
        {
            var result = new AndroidLoadingLogLine[Math.Min(count, maximumCount)];
            for (var index = 0; index < result.Length; index++)
            {
                var source = (next - 1 - index + lines.Length) % lines.Length;
                result[index] = lines[source];
            }
            return result;
        }
    }

    public void Clear()
    {
        lock (sync)
        {
            Array.Clear(lines);
            next = 0;
            count = 0;
        }
    }

    private void AppendLine(ConsoleLogLevel level, string text)
    {
        lines[next] = new AndroidLoadingLogLine(level, text);
        next = (next + 1) % lines.Length;
        if (count < lines.Length)
            count++;
    }
}
