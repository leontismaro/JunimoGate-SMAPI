using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI.AndroidHost;
using StardewModdingAPI.Framework;
using StardewModdingAPI.Internal.ConsoleWriting;
using StardewValley;

namespace StardewModdingAPI.Mobile;

internal static class AndroidModLoaderManager
{
    private static readonly object LoggerSync = new();
    static SpriteFont smallFont;
    static LocalizedContentManager content;
    private const int LoadingLogCapacity = 512;
    private static readonly AndroidLoadingLogBuffer LoadingLogs = new(LoadingLogCapacity);
    static float K_textLineHeight;
    internal static void TickUpdate()
    {
        //wait thread mod loader
        try
        {
            AndroidSModHooks.PumpMainThreadTasks();
        }
        catch (Exception ex)
        {
            Console.WriteLine("exception on task: " + ex);
        }
    }
    internal static void TryStartModEntry(IMod mod)
    {
        //main thread safe
        Task taskModEntry = AndroidSModHooks.AddTaskRunOnMainThread(
            () => mod.Entry(mod.Helper),
            $"Mod entry: {mod.GetType().FullName}");

        // log
        //Console.WriteLine("task id: " + taskModEntry.Id + ", mod name: " + mod.GetType());
        //Console.WriteLine("taskModEntry.Wait()...");

        taskModEntry.GetAwaiter().GetResult();
    }

    static int activeLoggerLeases;
    static bool IsShowLogger => Volatile.Read(ref activeLoggerLeases) > 0;
    static void ClearLogs() => LoadingLogs.Clear();
    internal static IDisposable AcquireLoggerToScreen()
    {
        if (AndroidHostServices.Options?.ShowLoadingLogsOnScreen == false)
            return LoggerLease.Empty;

        lock (LoggerSync)
        {
            if (content is null)
            {
                content = Game1.game1.CreateContentManager(Game1.content.ServiceProvider, Game1.content.RootDirectory);
                smallFont = content.Load<SpriteFont>("Fonts\\SmallFont");
                K_textLineHeight = smallFont.MeasureString("AAA").Y;
                SGameRunner.RegisterOnDraw(Draw);
            }
            if (activeLoggerLeases++ == 0)
                StardewModdingAPI.Framework.Monitor.RegisterOnLogImpl(OnLogImpl);
            ClearLogs();
        }
        return new LoggerLease(ReleaseLoggerToScreen);
    }

    private static void ReleaseLoggerToScreen()
    {
        if (AndroidHostServices.Options?.ShowLoadingLogsOnScreen == false)
            return;

        lock (LoggerSync)
        {
            if (activeLoggerLeases == 0)
                return;
            if (--activeLoggerLeases == 0)
            {
                StardewModdingAPI.Framework.Monitor.UnregisterOnLogImpl(OnLogImpl);
                ClearLogs();
            }
        }
    }

    static void OnLogImpl(ConsoleLogLevel logLevel, string msg)
        => LoadingLogs.Append(logLevel, msg);

    internal static void Draw(GameTime gameTime)
    {
        if (IsShowLogger is false)
        {
            return;
        }

        var spriteBatch = Game1.spriteBatch;
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

        var screenSize = Game1.game1.localMultiplayerWindow;
        Game1.game1.GraphicsDevice.Clear(Color.Black);
        Color lineColor = Color.White;
        const float K_fontScale = 1.3f;
        float lineHeight = K_fontScale * K_textLineHeight;
        int startDrawY = screenSize.Height - 30;
        var visibleLines = LoadingLogs.SnapshotNewestFirst(Math.Max(0, (int)(startDrawY / lineHeight)));
        for (int lineIndex = 0; lineIndex < visibleLines.Length; lineIndex++)
        {
            //draw from Left, Bottom
            Vector2 pos = Vector2.Zero;
            pos.Y = startDrawY - (lineHeight + (lineHeight * lineIndex));
            pos.X = 100;

            switch (visibleLines[lineIndex].Level)
            {
                case ConsoleLogLevel.Trace:
                case ConsoleLogLevel.Info:
                    lineColor = Color.White;
                    break;
                case ConsoleLogLevel.Alert:
                    lineColor = new(155, 56, 255);
                    break;
                case ConsoleLogLevel.Warn:
                    lineColor = new(255, 146, 56);
                    break;
                case ConsoleLogLevel.Error:
                    lineColor = new(255, 56, 70);
                    break;
            }

            spriteBatch.DrawString(smallFont, visibleLines[lineIndex].Text, pos, lineColor,
                0f, Vector2.Zero, K_fontScale, SpriteEffects.None, 10);

        }

        spriteBatch.End();
    }

    private sealed class LoggerLease : IDisposable
    {
        internal static readonly IDisposable Empty = new LoggerLease(null);
        private Action? release;

        internal LoggerLease(Action? release)
        {
            this.release = release;
        }

        public void Dispose()
            => Interlocked.Exchange(ref this.release, null)?.Invoke();
    }

}
