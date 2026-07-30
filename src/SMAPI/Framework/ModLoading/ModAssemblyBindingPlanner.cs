using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using StardewModdingAPI.AndroidHost;

namespace StardewModdingAPI.Framework.ModLoading;

/// <summary>Builds one deterministic binding plan from the local managed dependency closures actually referenced by enabled code mods.</summary>
internal static class ModAssemblyBindingPlanner
{
    public static ModAssemblyBindingPlan Build(
        IModMetadata[] orderedMods,
        ModAssemblyBindingPolicy policy,
        IMonitor monitor)
    {
        if (!Enum.IsDefined(typeof(ModAssemblyBindingPolicy), policy))
            throw new ArgumentOutOfRangeException(nameof(policy));

        var loadedNames = new HashSet<string>(
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetName().Name)
                .Where(name => name != null)!,
            StringComparer.OrdinalIgnoreCase);
        var analyses = new List<ModAnalysis>();
        var failures = new Dictionary<IModMetadata, string>();
        try
        {
            foreach ((IModMetadata mod, int order) in GetStableOrder(orderedMods).Select((mod, index) => (mod, index)))
            {
                if (mod.Status == ModMetadataStatus.Failed || mod.IsContentPack || string.IsNullOrWhiteSpace(mod.Manifest?.EntryDll))
                    continue;
                try
                {
                    analyses.Add(Analyze(mod, order, loadedNames));
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failures.TryAdd(mod, $"its managed assembly dependency closure could not be analyzed: {exception.Message}");
                }
            }

            var candidates = analyses.SelectMany(analysis => analysis.Assemblies).ToArray();
            RejectDuplicateEntries(candidates, failures);
            var selections = new Dictionary<string, ParsedAssembly>(StringComparer.OrdinalIgnoreCase);
            foreach (IGrouping<string, ParsedAssembly> group in candidates.GroupBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase))
            {
                ParsedAssembly[] active = group
                    .Where(candidate => !failures.ContainsKey(candidate.Owner))
                    .OrderBy(candidate => candidate.OwnerOrder)
                    .ThenBy(candidate => candidate.Owner.Manifest.UniqueID, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
                    .ToArray();
                if (active.Length == 0)
                    continue;

                ParsedAssembly? entry = active.FirstOrDefault(candidate => candidate.IsEntry);
                if (entry != null)
                {
                    foreach (ParsedAssembly candidate in active.Where(candidate => !candidate.IsEntry && !ReferenceEquals(candidate.Owner, entry.Owner)))
                        failures.TryAdd(candidate.Owner, Conflict(candidate.Name, "a Mod entry assembly"));
                    // Entry assemblies remain under SMAPI's manifest dependency order. The binding
                    // plan must never pull another Mod's entry into a consumer as a local DLL.
                    continue;
                }

                ParsedAssembly? selected = policy switch
                {
                    ModAssemblyBindingPolicy.Strict => SelectStrict(active, failures),
                    ModAssemblyBindingPolicy.FirstLoaded => active[0],
                    ModAssemblyBindingPolicy.HighestCompatible => SelectHighestCompatible(active, analyses, failures),
                    _ => throw new ArgumentOutOfRangeException(nameof(policy)),
                };
                if (selected != null)
                    selections[group.Key] = selected;
            }

            RemoveBindingsOwnedByFailedMods(selections, analyses, failures);
            var selectedPaths = selections.ToDictionary(
                selection => selection.Key,
                selection => selection.Value.Path,
                StringComparer.OrdinalIgnoreCase);

            monitor.Log(
                $"JunimoGate assembly binding plan: policy={policy}, managed candidates={candidates.Length}, selected identities={selectedPaths.Count}, rejected mods={failures.Count}.",
                failures.Count > 0 ? LogLevel.Warn : LogLevel.Debug);
            return new ModAssemblyBindingPlan(selectedPaths, failures);
        }
        finally
        {
            foreach (ModAnalysis analysis in analyses)
                analysis.Dispose();
        }
    }

    private static IModMetadata[] GetStableOrder(IModMetadata[] orderedMods)
    {
        var remaining = orderedMods
            .Where(mod => mod.Manifest != null)
            .Distinct()
            .ToList();
        var present = new Dictionary<string, IModMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (IModMetadata mod in remaining.Where(mod => !string.IsNullOrWhiteSpace(mod.Manifest.UniqueID)))
            present.TryAdd(mod.Manifest.UniqueID, mod);
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<IModMetadata>(remaining.Count);
        while (remaining.Count > 0)
        {
            IModMetadata[] ready = remaining
                .Where(mod => mod.Manifest.Dependencies
                    .Where(dependency => present.ContainsKey(dependency.UniqueID))
                    .All(dependency => emitted.Contains(dependency.UniqueID)))
                .OrderBy(mod => mod.Manifest.UniqueID, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (ready.Length == 0)
                ready = [remaining.OrderBy(mod => mod.Manifest.UniqueID, StringComparer.OrdinalIgnoreCase).First()];
            foreach (IModMetadata mod in ready)
            {
                remaining.Remove(mod);
                result.Add(mod);
                if (!string.IsNullOrWhiteSpace(mod.Manifest.UniqueID))
                    emitted.Add(mod.Manifest.UniqueID);
            }
        }
        return result.ToArray();
    }

    private static ModAnalysis Analyze(IModMetadata mod, int order, HashSet<string> loadedNames)
    {
        var analysis = new ModAnalysis(mod);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mod.DirectoryPath));
        string entry = Path.GetFullPath(Path.Combine(root, mod.Manifest.EntryDll));
        if (!entry.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The Mod entry assembly escaped its directory.");
        try
        {
            Visit(entry, isEntry: true);
            return analysis;
        }
        catch
        {
            analysis.Dispose();
            throw;
        }

        void Visit(string path, bool isEntry)
        {
            if (!File.Exists(path))
                return;
            byte[] bytes = File.ReadAllBytes(path);
            var definition = AssemblyDefinition.ReadAssembly(
                new MemoryStream(bytes, writable: false),
                new ReaderParameters(ReadingMode.Deferred) { InMemory = true });
            string name = definition.Name.Name;
            if (!visited.Add(name))
            {
                definition.Dispose();
                return;
            }

            var parsed = new ParsedAssembly(mod, order, Path.GetFullPath(path), bytes, definition, isEntry);
            analysis.Assemblies.Add(parsed);
            foreach (AssemblyNameReference dependency in definition.MainModule.AssemblyReferences)
            {
                if (loadedNames.Contains(dependency.Name))
                    continue;
                Visit(Path.Combine(Path.GetDirectoryName(path)!, $"{dependency.Name}.dll"), isEntry: false);
            }
        }
    }

    private static void RejectDuplicateEntries(
        ParsedAssembly[] candidates,
        IDictionary<IModMetadata, string> failures)
    {
        foreach (IGrouping<string, ParsedAssembly> group in candidates
                     .Where(candidate => candidate.IsEntry)
                     .GroupBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase))
        {
            ParsedAssembly[] entries = group
                .OrderBy(candidate => candidate.OwnerOrder)
                .ThenBy(candidate => candidate.Owner.Manifest.UniqueID, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (ParsedAssembly duplicate in entries.Skip(1))
                failures.TryAdd(duplicate.Owner, Conflict(group.Key, "another Mod entry assembly"));
        }
    }

    private static ParsedAssembly SelectStrict(
        ParsedAssembly[] candidates,
        IDictionary<IModMetadata, string> failures)
    {
        ParsedAssembly selected = candidates[0];
        foreach (ParsedAssembly candidate in candidates.Skip(1))
        {
            if (!BytesEqual(selected.Bytes, candidate.Bytes))
                failures.TryAdd(candidate.Owner, Conflict(candidate.Name, "a different DLL selected by Strict policy"));
        }
        return selected;
    }

    private static ParsedAssembly? SelectHighestCompatible(
        ParsedAssembly[] candidates,
        IReadOnlyList<ModAnalysis> analyses,
        IDictionary<IModMetadata, string> failures)
    {
        Version highest = candidates.Max(candidate => candidate.Definition.Name.Version ?? new Version(0, 0))!;
        ParsedAssembly[] highestCandidates = candidates
            .Where(candidate => Equals(candidate.Definition.Name.Version, highest))
            .ToArray();
        ParsedAssembly selected = highestCandidates[0];
        if (highestCandidates.Skip(1).Any(candidate => !BytesEqual(selected.Bytes, candidate.Bytes)))
        {
            foreach (ParsedAssembly candidate in candidates)
                failures.TryAdd(candidate.Owner, Conflict(candidate.Name, $"multiple different DLLs share highest AssemblyVersion {highest}"));
            FailConsumers(
                selected.Name,
                analyses,
                failures,
                $"its '{selected.Name}' dependency has no deterministic selection because multiple different DLLs share highest AssemblyVersion {highest}");
            return null;
        }

        var abi = new PublicAbi(selected.Definition);
        foreach (ModAnalysis analysis in analyses)
        {
            if (failures.ContainsKey(analysis.Mod))
                continue;
            foreach (ParsedAssembly consumer in analysis.Assemblies)
            {
                if (ReferenceEquals(consumer, selected))
                    continue;
                string? missing = abi.FindFirstMissingReference(consumer.Definition.MainModule, selected.Name);
                if (missing != null)
                {
                    failures.TryAdd(
                        analysis.Mod,
                        $"its '{selected.Name}' references are not compatible with selected AssemblyVersion {highest}: {missing}");
                    break;
                }
            }
        }
        return selected;
    }

    private static void RemoveBindingsOwnedByFailedMods(
        IDictionary<string, ParsedAssembly> selections,
        IReadOnlyList<ModAnalysis> analyses,
        IDictionary<IModMetadata, string> failures)
    {
        bool removed;
        do
        {
            removed = false;
            foreach ((string name, ParsedAssembly selected) in selections.ToArray())
            {
                if (!failures.ContainsKey(selected.Owner))
                    continue;

                selections.Remove(name);
                removed = true;
                FailConsumers(
                    name,
                    analyses,
                    failures,
                    $"its '{name}' dependency was selected from a Mod which was rejected");
            }
        }
        while (removed);
    }

    private static void FailConsumers(
        string assemblyName,
        IReadOnlyList<ModAnalysis> analyses,
        IDictionary<IModMetadata, string> failures,
        string reason)
    {
        foreach (ModAnalysis analysis in analyses)
        {
            if (analysis.Assemblies.Any(assembly => assembly.Definition.MainModule.AssemblyReferences.Any(reference =>
                    reference.Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase))))
            {
                failures.TryAdd(analysis.Mod, reason);
            }
        }
    }

    private static bool BytesEqual(byte[] left, byte[] right) =>
        left.Length == right.Length && left.AsSpan().SequenceEqual(right);

    private static string Conflict(string name, string reason) =>
        $"its managed assembly identity '{name}' conflicts with {reason}";

    private sealed class ModAnalysis : IDisposable
    {
        public ModAnalysis(IModMetadata mod) => this.Mod = mod;
        public IModMetadata Mod { get; }
        public List<ParsedAssembly> Assemblies { get; } = [];
        public void Dispose()
        {
            foreach (ParsedAssembly assembly in this.Assemblies)
                assembly.Definition.Dispose();
        }
    }

    private sealed record ParsedAssembly(
        IModMetadata Owner,
        int OwnerOrder,
        string Path,
        byte[] Bytes,
        AssemblyDefinition Definition,
        bool IsEntry)
    {
        public string Name => this.Definition.Name.Name;
    }

    private sealed class PublicAbi
    {
        private readonly string assemblyName;
        private readonly Dictionary<string, TypeDefinition> types;

        public PublicAbi(AssemblyDefinition assembly)
        {
            this.assemblyName = assembly.Name.Name;
            this.types = assembly.MainModule.GetTypes()
                .Where(IsExternallyVisible)
                .ToDictionary(type => type.FullName, StringComparer.Ordinal);
        }

        public string? FindFirstMissingReference(ModuleDefinition consumer, string assemblyName)
        {
            foreach (TypeReference type in consumer.GetTypeReferences())
            {
                if (HasAssemblyScope(type, assemblyName) && !this.types.ContainsKey(GetNamedType(type).FullName))
                    return $"missing type {type.FullName}";
            }
            foreach (MemberReference member in consumer.GetMemberReferences())
            {
                if (HasAssemblyScope(member.DeclaringType, assemblyName) && !this.Contains(member))
                    return $"missing member {member.FullName}";
            }
            return null;
        }

        private bool Contains(MemberReference reference)
        {
            TypeReference declaringReference = reference.DeclaringType;
            TypeReference declaringElement = GetNamedType(declaringReference);
            if (!this.types.TryGetValue(declaringElement.FullName, out TypeDefinition? declaringType))
                return false;
            TypeReference[] typeArguments = declaringReference is GenericInstanceType genericType
                ? genericType.GenericArguments.ToArray()
                : [];

            string[] formattedTypeArguments = typeArguments
                .Select(argument => FormatType(argument, [], [], this.assemblyName))
                .ToArray();

            if (reference is FieldReference field)
            {
                string expectedType = FormatType(field.FieldType, formattedTypeArguments, [], this.assemblyName);
                return this.FindMember(declaringType, formattedTypeArguments, (candidateType, candidateTypeArguments) =>
                    candidateType.Fields.Any(candidate =>
                        IsExternallyVisible(candidate) && candidate.Name == field.Name &&
                        FormatType(candidate.FieldType, candidateTypeArguments, [], this.assemblyName) == expectedType));
            }

            if (reference is MethodReference methodReference)
            {
                GenericInstanceMethod? genericMethod = methodReference as GenericInstanceMethod;
                MethodReference method = genericMethod?.ElementMethod ?? methodReference;
                string[] methodArguments = genericMethod?.GenericArguments
                    .Select(argument => FormatType(argument, formattedTypeArguments, [], this.assemblyName))
                    .ToArray() ?? [];
                string expectedReturnType = FormatType(method.ReturnType, formattedTypeArguments, methodArguments, this.assemblyName);
                string[] expectedParameters = method.Parameters
                    .Select(parameter => FormatType(parameter.ParameterType, formattedTypeArguments, methodArguments, this.assemblyName))
                    .ToArray();
                bool includeInherited = method.Name is not ".ctor" and not ".cctor";
                return this.FindMember(declaringType, formattedTypeArguments, (candidateType, candidateTypeArguments) =>
                    candidateType.Methods.Any(candidate =>
                        IsExternallyVisible(candidate) && candidate.Name == method.Name &&
                        candidate.HasThis == method.HasThis &&
                        candidate.GenericParameters.Count == method.GenericParameters.Count &&
                        candidate.Parameters.Count == method.Parameters.Count &&
                        FormatType(candidate.ReturnType, candidateTypeArguments, methodArguments, this.assemblyName) == expectedReturnType &&
                        candidate.Parameters.Select(parameter => FormatType(parameter.ParameterType, candidateTypeArguments, methodArguments, this.assemblyName))
                            .SequenceEqual(expectedParameters, StringComparer.Ordinal)), includeInherited);
            }
            return false;
        }

        private bool FindMember(
            TypeDefinition declaringType,
            string[] typeArguments,
            Func<TypeDefinition, string[], bool> matches,
            bool includeInherited = true)
        {
            var pending = new Queue<(TypeDefinition Type, string[] TypeArguments)>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            pending.Enqueue((declaringType, typeArguments));
            while (pending.Count > 0)
            {
                (TypeDefinition type, string[] arguments) = pending.Dequeue();
                string key = $"{type.FullName}<{string.Join(",", arguments)}>";
                if (!visited.Add(key))
                    continue;
                if (matches(type, arguments))
                    return true;
                if (!includeInherited)
                    continue;

                this.EnqueueInheritedType(type.BaseType, arguments, pending);
                foreach (InterfaceImplementation implementation in type.Interfaces)
                    this.EnqueueInheritedType(implementation.InterfaceType, arguments, pending);
            }
            return false;
        }

        private void EnqueueInheritedType(
            TypeReference? reference,
            IReadOnlyList<string> declaringArguments,
            Queue<(TypeDefinition Type, string[] TypeArguments)> pending)
        {
            if (reference == null || !HasAssemblyScope(reference, this.assemblyName))
                return;

            TypeReference namedType = GetNamedType(reference);
            if (!this.types.TryGetValue(namedType.FullName, out TypeDefinition? definition))
                return;

            string[] inheritedArguments = reference is GenericInstanceType generic
                ? generic.GenericArguments
                    .Select(argument => FormatType(argument, declaringArguments, [], this.assemblyName))
                    .ToArray()
                : [];
            pending.Enqueue((definition, inheritedArguments));
        }

        private static bool HasAssemblyScope(TypeReference type, string assemblyName)
        {
            TypeReference element = GetNamedType(type);
            while (element.DeclaringType != null)
                element = element.DeclaringType;
            IMetadataScope? scope = element.Scope;
            string? scopedAssemblyName = scope switch
            {
                AssemblyNameReference assembly => assembly.Name,
                ModuleDefinition module => module.Assembly?.Name.Name,
                _ => null,
            };
            return scopedAssemblyName?.Equals(assemblyName, StringComparison.OrdinalIgnoreCase) == true;
        }

        private static TypeReference GetNamedType(TypeReference type)
        {
            while (type is TypeSpecification specification)
                type = specification.ElementType;
            return type;
        }

        private static bool IsExternallyVisible(TypeDefinition type) =>
            type.DeclaringType == null
                ? type.IsPublic
                : IsExternallyVisible(type.DeclaringType) &&
                  (type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamilyOrAssembly);

        private static bool IsExternallyVisible(FieldDefinition field) =>
            field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;

        private static bool IsExternallyVisible(MethodDefinition method) =>
            method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

        private static string FormatType(
            TypeReference type,
            IReadOnlyList<string> typeArguments,
            IReadOnlyList<string> methodArguments,
            string unifiedAssemblyName)
        {
            if (type is GenericParameter parameter)
            {
                IReadOnlyList<string> arguments = parameter.Type == GenericParameterType.Type
                    ? typeArguments
                    : methodArguments;
                return parameter.Position < arguments.Count
                    ? arguments[parameter.Position]
                    : $"{(parameter.Type == GenericParameterType.Type ? "!" : "!!")}{parameter.Position}";
            }
            if (type is GenericInstanceType generic)
                return $"{FormatType(generic.ElementType, typeArguments, methodArguments, unifiedAssemblyName)}<{string.Join(",", generic.GenericArguments.Select(argument => FormatType(argument, typeArguments, methodArguments, unifiedAssemblyName)))}>";
            if (type is ArrayType array)
                return $"{FormatType(array.ElementType, typeArguments, methodArguments, unifiedAssemblyName)}[{new string(',', array.Rank - 1)}]";
            if (type is ByReferenceType byReference)
                return $"{FormatType(byReference.ElementType, typeArguments, methodArguments, unifiedAssemblyName)}&";
            if (type is PointerType pointer)
                return $"{FormatType(pointer.ElementType, typeArguments, methodArguments, unifiedAssemblyName)}*";
            if (type is OptionalModifierType optional)
                return $"modopt({FormatType(optional.ModifierType, typeArguments, methodArguments, unifiedAssemblyName)}){FormatType(optional.ElementType, typeArguments, methodArguments, unifiedAssemblyName)}";
            if (type is RequiredModifierType required)
                return $"modreq({FormatType(required.ModifierType, typeArguments, methodArguments, unifiedAssemblyName)}){FormatType(required.ElementType, typeArguments, methodArguments, unifiedAssemblyName)}";
            if (type is PinnedType pinned)
                return $"pinned({FormatType(pinned.ElementType, typeArguments, methodArguments, unifiedAssemblyName)})";
            if (type is SentinelType sentinel)
                return $"sentinel({FormatType(sentinel.ElementType, typeArguments, methodArguments, unifiedAssemblyName)})";
            return $"[{FormatAssemblyScope(type, unifiedAssemblyName)}]{type.FullName}";
        }

        private static string FormatAssemblyScope(TypeReference type, string unifiedAssemblyName)
        {
            TypeReference element = GetNamedType(type);
            while (element.DeclaringType != null)
                element = element.DeclaringType;
            AssemblyNameReference? assembly = element.Scope switch
            {
                AssemblyNameReference reference => reference,
                ModuleDefinition module => module.Assembly?.Name,
                _ => null,
            };
            if (assembly == null)
                return $"{element.Scope?.MetadataScopeType}:{element.Scope?.Name}";
            // The selected identity intentionally unifies versions; all other scopes retain
            // their complete assembly identity so same-named types can't compare equal.
            return assembly.Name.Equals(unifiedAssemblyName, StringComparison.OrdinalIgnoreCase)
                ? $"assembly:{unifiedAssemblyName}"
                : $"assembly:{assembly.FullName}";
        }
    }
}

internal sealed class ModAssemblyBindingPlan
{
    private readonly IReadOnlyDictionary<string, string> selectedPaths;

    public ModAssemblyBindingPlan(
        IReadOnlyDictionary<string, string> selectedPaths,
        IReadOnlyDictionary<IModMetadata, string> failures)
    {
        this.selectedPaths = selectedPaths;
        this.Failures = failures;
    }

    public IReadOnlyDictionary<IModMetadata, string> Failures { get; }

    public bool TryResolve(string simpleName, out string path) =>
        this.selectedPaths.TryGetValue(simpleName, out path!);
}
