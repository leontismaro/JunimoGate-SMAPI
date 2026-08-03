using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using StardewModdingAPI.Toolkit.Framework.ModData;

namespace StardewModdingAPI.Framework.ModLoading;

/// <summary>Caches deterministic Mod rewrite results in app-private storage.</summary>
internal sealed class ModRewriteCache
{
    public const string RewriteSchema = "junimogate-mod-rewrite/v3";
    private const int FileVersion = 3;
    private const int MaximumPayloadBytes = 256 * 1024 * 1024;
    private const int MaximumReferenceCount = 4096;
    private const int MaximumReferenceBytes = 16 * 1024;
    private const ModWarning CacheableWarnings =
        ModWarning.ChangesSaveSerializer |
        ModWarning.PatchesGame |
        ModWarning.UsesUnvalidatedUpdateTick |
        ModWarning.AccessesConsole |
        ModWarning.AccessesFilesystem |
        ModWarning.AccessesShell;
    private static readonly byte[] Magic = "JGMRC3\0\0"u8.ToArray();
    private readonly string root;
    private readonly byte[] contextBytes;

    public ModRewriteCache(string root, string contextIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextIdentity);
        this.root = Path.GetFullPath(root);
        this.contextBytes = Encoding.UTF8.GetBytes(contextIdentity);
        Directory.CreateDirectory(this.root);
    }

    public bool TryRead(
        ReadOnlyMemory<byte> sourceBytes,
        ReadOnlyMemory<byte>? sourceSymbols,
        out CachedRewrite rewrite)
    {
        string path = this.GetEntryPath(
            sourceBytes.Span,
            sourceSymbols is { } symbolData ? symbolData.Span : default);
        rewrite = default;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < Magic.Length + sizeof(int) * 5 || stream.Length > MaximumPayloadBytes * 2L)
                throw new InvalidDataException("The Mod rewrite cache entry has an invalid length.");
            Span<byte> magic = stackalloc byte[Magic.Length];
            stream.ReadExactly(magic);
            if (!magic.SequenceEqual(Magic))
                throw new InvalidDataException("The Mod rewrite cache entry has an invalid header.");
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (reader.ReadInt32() != FileVersion)
                throw new InvalidDataException("The Mod rewrite cache entry has an unsupported version.");
            int assemblyLength = reader.ReadInt32();
            int symbolLength = reader.ReadInt32();
            int referenceCount = reader.ReadInt32();
            var warnings = (ModWarning)reader.ReadInt32();
            if (assemblyLength is < 0 or > MaximumPayloadBytes || symbolLength is < 0 or > MaximumPayloadBytes ||
                referenceCount is < 0 or > MaximumReferenceCount || (warnings & ~CacheableWarnings) != 0)
            {
                throw new InvalidDataException("The Mod rewrite cache entry has invalid payload lengths.");
            }

            var references = new AssemblyNameReference[referenceCount];
            for (int index = 0; index < references.Length; index++)
            {
                int length = reader.ReadInt32();
                if (length is < 1 or > MaximumReferenceBytes || stream.Length - stream.Position < length)
                    throw new InvalidDataException("The Mod rewrite cache entry has an invalid assembly reference.");
                byte[] bytes = reader.ReadBytes(length);
                string reference = Encoding.UTF8.GetString(bytes);
                if (string.IsNullOrWhiteSpace(reference))
                    throw new InvalidDataException("The Mod rewrite cache entry has an empty assembly reference.");
                references[index] = AssemblyNameReference.Parse(reference);
            }
            if (stream.Length - stream.Position != (long)assemblyLength + symbolLength)
                throw new InvalidDataException("The Mod rewrite cache entry has invalid payload lengths.");

            byte[]? assembly = assemblyLength == 0 ? null : reader.ReadBytes(assemblyLength);
            byte[]? symbols = symbolLength == 0 ? null : reader.ReadBytes(symbolLength);
            rewrite = new CachedRewrite(assembly, symbols, references, warnings);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            TryDelete(path);
            return false;
        }
    }

    public bool TryStore(
        ReadOnlyMemory<byte> sourceBytes,
        ReadOnlyMemory<byte>? sourceSymbols,
        ReadOnlyMemory<byte>? rewrittenAssembly,
        ReadOnlyMemory<byte>? symbols,
        IReadOnlyList<string> references,
        ModWarning warnings,
        bool isCacheable)
    {
        if (!isCacheable || (warnings & ~CacheableWarnings) != 0)
            return false;
        int assemblyLength = rewrittenAssembly?.Length ?? 0;
        int symbolLength = symbols?.Length ?? 0;
        if (assemblyLength > MaximumPayloadBytes || symbolLength > MaximumPayloadBytes)
            return false;
        byte[][] referenceBytes = references.Select(Encoding.UTF8.GetBytes).ToArray();
        if (referenceBytes.Length > MaximumReferenceCount || referenceBytes.Any(bytes => bytes.Length is < 1 or > MaximumReferenceBytes))
            return false;

        string path = this.GetEntryPath(
            sourceBytes.Span,
            sourceSymbols is { } sourceSymbolData ? sourceSymbolData.Span : default);
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       64 * 1024,
                       FileOptions.SequentialScan))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(FileVersion);
                writer.Write(assemblyLength);
                writer.Write(symbolLength);
                writer.Write(referenceBytes.Length);
                writer.Write((int)warnings);
                foreach (byte[] reference in referenceBytes)
                {
                    writer.Write(reference.Length);
                    writer.Write(reference);
                }
                if (rewrittenAssembly is { } assembly)
                    stream.Write(assembly.Span);
                if (symbols is { } symbolData)
                    stream.Write(symbolData.Span);
                stream.Flush();
            }
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    internal string GetEntryPathForTests(
        ReadOnlyMemory<byte> sourceBytes,
        ReadOnlyMemory<byte>? sourceSymbols = null) =>
        this.GetEntryPath(
            sourceBytes.Span,
            sourceSymbols is { } symbolData ? symbolData.Span : default);

    private string GetEntryPath(ReadOnlySpan<byte> sourceBytes, ReadOnlySpan<byte> sourceSymbols)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(RewriteSchema));
        hash.AppendData([0]);
        hash.AppendData(this.contextBytes);
        hash.AppendData([0]);
        AppendLength(sourceBytes.Length);
        hash.AppendData(sourceBytes);
        AppendLength(sourceSymbols.Length);
        hash.AppendData(sourceSymbols);
        return Path.Combine(this.root, Convert.ToHexStringLower(hash.GetHashAndReset()) + ".jgmrc");

        void AppendLength(int length)
        {
            Span<byte> bytes = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, length);
            hash.AppendData(bytes);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    internal readonly record struct CachedRewrite(
        byte[]? AssemblyBytes,
        byte[]? SymbolBytes,
        IReadOnlyList<AssemblyNameReference> AssemblyReferences,
        ModWarning Warnings)
    {
        public bool Changed => this.AssemblyBytes is not null;
    }
}
