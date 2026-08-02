using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Mono.Cecil;
using StardewModdingAPI.Framework;
using StardewModdingAPI.Internal;

namespace StardewModdingAPI.Mobile;

internal sealed class AndroidModFixManager
{
    private sealed class CompatibilityCallbacks
    {
        public List<Action<Assembly>> AssemblyLoaded { get; } = [];
        public List<Action<AssemblyDefinition>> RewriteAssembly { get; } = [];
        public List<Action<IMod>> AfterModEntry { get; } = [];
    }

    private static readonly object InstanceLock = new();
    private static AndroidModFixManager? instance;

    private readonly object registryLock = new();
    private readonly Dictionary<string, CompatibilityCallbacks> registry = new(StringComparer.OrdinalIgnoreCase);

    private AndroidModFixManager(IMonitor monitor)
    {
        Monitor = monitor;
    }

    public static AndroidModFixManager Instance => instance
        ?? throw new InvalidOperationException("Android Mod compatibility has not been initialized.");

    public IMonitor Monitor { get; }

    public static AndroidModFixManager Init()
    {
        var manager = new AndroidModFixManager(SCore.Instance.SMAPIMonitor);
        lock (InstanceLock)
        {
            if (instance is not null)
                AppDomain.CurrentDomain.AssemblyLoad -= instance.OnAssemblyLoaded;

            instance = manager;
            AppDomain.CurrentDomain.AssemblyLoad += manager.OnAssemblyLoaded;
        }

        return manager;
    }

    public void RegisterOnModLoaded(string assemblyName, Action<Assembly> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (registryLock)
            GetOrCreate(assemblyName).AssemblyLoaded.Add(callback);
    }

    internal void RegisterRewriteModAssemblyDef(string assemblyName, Action<AssemblyDefinition> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (registryLock)
            GetOrCreate(assemblyName).RewriteAssembly.Add(callback);
    }

    internal void RegisterOnPostModEntry(string assemblyName, Action<IMod> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (registryLock)
            GetOrCreate(assemblyName).AfterModEntry.Add(callback);
    }

    internal void TryRewriteMod(
        Framework.ModLoading.AssemblyParseResult assembly,
        out bool hasRewrite,
        out Exception? exception)
    {
        string assemblyName = assembly.Definition.Name.Name;
        Action<AssemblyDefinition>[] callbacks;
        lock (registryLock)
        {
            if (!registry.TryGetValue(NormalizeAssemblyName(assemblyName), out var registration) ||
                registration.RewriteAssembly.Count == 0)
            {
                hasRewrite = false;
                exception = null;
                return;
            }

            callbacks = registration.RewriteAssembly.ToArray();
            registration.RewriteAssembly.Clear();
        }

        hasRewrite = false;
        exception = null;
        foreach (var callback in callbacks)
        {
            Monitor.Log("Try ModFixManager rewrite mod: " + assembly.Definition.Name);
            try
            {
                callback(assembly.Definition);
                hasRewrite = true;
                Monitor.Log("Done rewrite mod: " + assembly.Definition.Name);
            }
            catch (Exception ex)
            {
                Monitor.Log(ex.ToString(), LogLevel.Error);
                exception = ex;
                return;
            }
        }
    }

    internal void OnPostfixModEntry(IMod mod)
    {
        string? assemblyName = mod.GetType().Assembly.GetName().Name;
        if (assemblyName is null)
            return;

        foreach (var callback in GetCallbacks(assemblyName, static registration => registration.AfterModEntry))
        {
            try
            {
                callback(mod);
            }
            catch (Exception ex)
            {
                Monitor.Log(ex.GetLogSummary(), LogLevel.Error);
            }
        }
    }

    private void OnAssemblyLoaded(object? sender, AssemblyLoadEventArgs args)
    {
        string? assemblyName = args.LoadedAssembly.GetName().Name;
        if (assemblyName is null)
            return;

        foreach (var callback in GetCallbacks(assemblyName, static registration => registration.AssemblyLoaded))
        {
            try
            {
                callback(args.LoadedAssembly);
            }
            catch (Exception ex)
            {
                Monitor.Log(ex.ToString(), LogLevel.Error);
            }
        }
    }

    private Action<T>[] GetCallbacks<T>(
        string assemblyName,
        Func<CompatibilityCallbacks, List<Action<T>>> select)
    {
        lock (registryLock)
        {
            return registry.TryGetValue(NormalizeAssemblyName(assemblyName), out var registration)
                ? select(registration).ToArray()
                : [];
        }
    }

    private CompatibilityCallbacks GetOrCreate(string assemblyName)
    {
        string key = NormalizeAssemblyName(assemblyName);
        if (!registry.TryGetValue(key, out var registration))
        {
            registration = new CompatibilityCallbacks();
            registry.Add(key, registration);
        }

        return registration;
    }

    private static string NormalizeAssemblyName(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        string name = Path.GetFileName(assemblyName.Trim());
        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        if (name.Length == 0)
            throw new ArgumentException("The assembly name is empty.", nameof(assemblyName));
        return name;
    }
}
