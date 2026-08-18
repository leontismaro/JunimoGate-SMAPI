using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Android.OS;

namespace StardewModdingAPI.Mobile;

/// <summary>Records bounded diagnostics when the Android UI looper or a synchronous game operation stalls.</summary>
internal static class AndroidRuntimeDiagnostics
{
    private const long HeartbeatIntervalMilliseconds = 250;
    private static readonly TimeSpan HeartbeatWarningThreshold = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WarningCooldown = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SlowOperationThreshold = TimeSpan.FromMilliseconds(250);
    private static readonly object Gate = new();

    private static IMonitor? monitor;
    private static Handler? handler;
    private static HeartbeatRunnable? heartbeatRunnable;
    private static Timer? watchdog;
    private static int started;
    private static int uiManagedThreadId;
    private static long lastHeartbeatAt;
    private static long lastWarningAt;
    private static long nextOperationToken;
    private static long currentOperationToken;
    private static long currentOperationStartedAt;
    private static string? currentCategory;
    private static string? currentName;
    private static string? currentDetail;
    private static MethodBase? currentMethod;

    internal static void Start(IMonitor runtimeMonitor)
    {
        ArgumentNullException.ThrowIfNull(runtimeMonitor);
        Stop();

        Handler newHandler = new(Looper.MainLooper
            ?? throw new InvalidOperationException("The Android main looper is unavailable."));
        HeartbeatRunnable newHeartbeat = new();
        lock (Gate)
        {
            monitor = runtimeMonitor;
            handler = newHandler;
            heartbeatRunnable = newHeartbeat;
            uiManagedThreadId = Looper.MyLooper() == Looper.MainLooper
                ? System.Environment.CurrentManagedThreadId
                : 0;
            lastHeartbeatAt = Stopwatch.GetTimestamp();
            lastWarningAt = 0;
            currentOperationToken = 0;
            currentOperationStartedAt = 0;
            currentCategory = null;
            currentName = null;
            currentDetail = null;
            currentMethod = null;
            Volatile.Write(ref started, 1);
            watchdog = new Timer(WatchdogTick, null, HeartbeatIntervalMilliseconds, HeartbeatIntervalMilliseconds);
        }

        newHandler.Post(newHeartbeat);
        SafeLog("android-runtime-diagnostic started", LogLevel.Debug);
    }

    internal static void Stop()
    {
        if (Interlocked.Exchange(ref started, 0) == 0)
            return;

        Handler? oldHandler;
        HeartbeatRunnable? oldHeartbeat;
        Timer? oldWatchdog;
        lock (Gate)
        {
            oldHandler = handler;
            oldHeartbeat = heartbeatRunnable;
            oldWatchdog = watchdog;
            handler = null;
            heartbeatRunnable = null;
            watchdog = null;
            uiManagedThreadId = 0;
            currentOperationToken = 0;
            currentOperationStartedAt = 0;
            currentCategory = null;
            currentName = null;
            currentDetail = null;
            currentMethod = null;
        }

        if (oldHandler is not null && oldHeartbeat is not null)
            oldHandler.RemoveCallbacks(oldHeartbeat);
        oldWatchdog?.Dispose();
        SafeLog("android-runtime-diagnostic stopped", LogLevel.Debug);
        lock (Gate)
            monitor = null;
    }

    internal static OperationScope Track(
        string category,
        string name,
        string? detail = null,
        MethodBase? method = null)
    {
        if (Volatile.Read(ref started) == 0
            || System.Environment.CurrentManagedThreadId != Volatile.Read(ref uiManagedThreadId))
        {
            return default;
        }

        long startedAt = Stopwatch.GetTimestamp();
        lock (Gate)
        {
            if (Volatile.Read(ref started) == 0)
                return default;

            long token = ++nextOperationToken;
            OperationScope scope = new(
                token,
                startedAt,
                category,
                name,
                detail,
                method,
                currentOperationToken,
                currentOperationStartedAt,
                currentCategory,
                currentName,
                currentDetail,
                currentMethod);
            currentOperationToken = token;
            currentOperationStartedAt = startedAt;
            currentCategory = category;
            currentName = name;
            currentDetail = detail;
            currentMethod = method;
            return scope;
        }
    }

    private static void HeartbeatTick()
    {
        if (Volatile.Read(ref started) == 0)
            return;

        Volatile.Write(ref uiManagedThreadId, System.Environment.CurrentManagedThreadId);
        Volatile.Write(ref lastHeartbeatAt, Stopwatch.GetTimestamp());

        Handler? currentHandler;
        HeartbeatRunnable? currentHeartbeat;
        lock (Gate)
        {
            currentHandler = handler;
            currentHeartbeat = heartbeatRunnable;
        }
        if (Volatile.Read(ref started) != 0 && currentHandler is not null && currentHeartbeat is not null)
            currentHandler.PostDelayed(currentHeartbeat, HeartbeatIntervalMilliseconds);
    }

    private static void WatchdogTick(object? state)
    {
        if (Volatile.Read(ref started) == 0)
            return;

        long now = Stopwatch.GetTimestamp();
        TimeSpan delay = Stopwatch.GetElapsedTime(Volatile.Read(ref lastHeartbeatAt), now);
        if (delay < HeartbeatWarningThreshold)
            return;

        long previousWarning = Volatile.Read(ref lastWarningAt);
        if (previousWarning != 0 && Stopwatch.GetElapsedTime(previousWarning, now) < WarningCooldown)
            return;
        if (Interlocked.CompareExchange(ref lastWarningAt, now, previousWarning) != previousWarning)
            return;

        string operation;
        lock (Gate)
        {
            operation = currentOperationToken == 0
                ? "operation=<none>"
                : FormatOperation(
                    currentCategory,
                    currentName,
                    currentDetail,
                    currentMethod,
                    Stopwatch.GetElapsedTime(currentOperationStartedAt, now));
        }

        long managedBytes = GC.GetTotalMemory(forceFullCollection: false);
        long workingSetBytes;
        try
        {
            workingSetBytes = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        }
        catch
        {
            workingSetBytes = -1;
        }

        SafeLog(
            $"android-runtime-diagnostic ui-looper-stall delayMs={delay.TotalMilliseconds:F0} {operation} managedMiB={ToMebibytes(managedBytes):F1} rssMiB={ToMebibytes(workingSetBytes):F1}",
            LogLevel.Warn);
    }

    private static void EndOperation(OperationScope scope)
    {
        long now = Stopwatch.GetTimestamp();
        TimeSpan elapsed = Stopwatch.GetElapsedTime(scope.StartedAt, now);
        lock (Gate)
        {
            if (currentOperationToken == scope.Token)
            {
                currentOperationToken = scope.PreviousToken;
                currentOperationStartedAt = scope.PreviousStartedAt;
                currentCategory = scope.PreviousCategory;
                currentName = scope.PreviousName;
                currentDetail = scope.PreviousDetail;
                currentMethod = scope.PreviousMethod;
            }
        }

        if (elapsed >= SlowOperationThreshold)
        {
            SafeLog(
                $"android-runtime-diagnostic slow-operation {FormatOperation(scope.Category, scope.Name, scope.Detail, scope.Method, elapsed)}",
                LogLevel.Warn);
        }
    }

    private static string FormatOperation(
        string? category,
        string? name,
        string? detail,
        MethodBase? method,
        TimeSpan elapsed)
    {
        string methodName = method is null
            ? "<none>"
            : $"{method.DeclaringType?.FullName ?? "<unknown>"}.{method.Name}";
        return $"elapsedMs={elapsed.TotalMilliseconds:F0} category='{category ?? "<none>"}' name='{name ?? "<none>"}' detail='{detail ?? "<none>"}' method='{methodName}'";
    }

    private static double ToMebibytes(long bytes) => bytes < 0 ? -1 : bytes / 1024d / 1024d;

    private static void SafeLog(string message, LogLevel level)
    {
        try
        {
            IMonitor? currentMonitor;
            lock (Gate)
                currentMonitor = monitor;
            currentMonitor?.Log(message, level);
        }
        catch
        {
            // Diagnostics must not affect the game loop.
        }
    }

    internal readonly struct OperationScope : IDisposable
    {
        internal long Token { get; }
        internal long StartedAt { get; }
        internal string? Category { get; }
        internal string? Name { get; }
        internal string? Detail { get; }
        internal MethodBase? Method { get; }
        internal long PreviousToken { get; }
        internal long PreviousStartedAt { get; }
        internal string? PreviousCategory { get; }
        internal string? PreviousName { get; }
        internal string? PreviousDetail { get; }
        internal MethodBase? PreviousMethod { get; }

        internal OperationScope(
            long token,
            long startedAt,
            string category,
            string name,
            string? detail,
            MethodBase? method,
            long previousToken,
            long previousStartedAt,
            string? previousCategory,
            string? previousName,
            string? previousDetail,
            MethodBase? previousMethod)
        {
            this.Token = token;
            this.StartedAt = startedAt;
            this.Category = category;
            this.Name = name;
            this.Detail = detail;
            this.Method = method;
            this.PreviousToken = previousToken;
            this.PreviousStartedAt = previousStartedAt;
            this.PreviousCategory = previousCategory;
            this.PreviousName = previousName;
            this.PreviousDetail = previousDetail;
            this.PreviousMethod = previousMethod;
        }

        public void Dispose()
        {
            if (this.Token != 0)
                EndOperation(this);
        }
    }

    private sealed class HeartbeatRunnable : Java.Lang.Object, Java.Lang.IRunnable
    {
        public void Run() => HeartbeatTick();
    }
}
