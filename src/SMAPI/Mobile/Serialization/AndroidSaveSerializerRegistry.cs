using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;

namespace StardewModdingAPI.Mobile.Serialization;

internal sealed class AndroidSaveSerializerRegistry
{
    private readonly IDictionary<Type, XmlSerializer> Serializers;
    private readonly Func<Type, XmlSerializer> ResolveSerializer;

    private AndroidSaveSerializerRegistry(
        IDictionary<Type, XmlSerializer> serializers,
        Func<Type, XmlSerializer> resolveSerializer)
    {
        this.Serializers = serializers;
        this.ResolveSerializer = resolveSerializer;
    }

    public static AndroidSaveSerializerRegistry Create(
        Type serializerOwner,
        Func<Type, XmlSerializer> resolveSerializer)
    {
        if (serializerOwner is null)
            throw new ArgumentNullException(nameof(serializerOwner));
        if (resolveSerializer is null)
            throw new ArgumentNullException(nameof(resolveSerializer));

        FieldInfo[] candidates = serializerOwner
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => typeof(IDictionary<Type, XmlSerializer>).IsAssignableFrom(field.FieldType))
            .ToArray();
        if (candidates.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected one static Type-to-XmlSerializer cache on {serializerOwner.FullName}, found {candidates.Length}.");
        }

        object? value = candidates[0].GetValue(null);
        if (value is not IDictionary<Type, XmlSerializer> serializers)
        {
            throw new InvalidOperationException(
                $"The serializer cache {serializerOwner.FullName}.{candidates[0].Name} is unavailable.");
        }

        return new AndroidSaveSerializerRegistry(serializers, resolveSerializer);
    }

    public XmlSerializer Get(Type rootType)
    {
        if (rootType is null)
            throw new ArgumentNullException(nameof(rootType));

        return this.ResolveSerializer(rootType)
            ?? throw new InvalidOperationException($"The Android save serializer for {rootType.FullName} is unavailable.");
    }

    public void Set(Type rootType, XmlSerializer serializer)
    {
        if (rootType is null)
            throw new ArgumentNullException(nameof(rootType));
        if (serializer is null)
            throw new ArgumentNullException(nameof(serializer));

        lock (this.Serializers)
        {
            bool hadPrevious = this.Serializers.TryGetValue(rootType, out XmlSerializer? previous);
            this.Serializers[rootType] = serializer;
            try
            {
                XmlSerializer resolved = this.ResolveSerializer(rootType);
                if (!ReferenceEquals(serializer, resolved))
                {
                    throw new InvalidOperationException(
                        $"The Android save serializer override for {rootType.FullName} was not observed by the game lookup.");
                }
            }
            catch
            {
                if (hadPrevious)
                    this.Serializers[rootType] = previous!;
                else
                    this.Serializers.Remove(rootType);
                throw;
            }
        }
    }
}
