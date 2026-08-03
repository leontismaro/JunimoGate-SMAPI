using System;
using System.Xml.Serialization;
using StardewValley.SaveSerialization;

namespace StardewModdingAPI.Mobile.Serialization;

internal static class AndroidSaveSerializerBridge
{
    private static readonly Lazy<AndroidSaveSerializerRegistry> Registry = new(
        () => AndroidSaveSerializerRegistry.Create(typeof(SaveSerializer), SaveSerializer.GetSerializer));

    public static XmlSerializer Get(Type rootType) => Registry.Value.Get(rootType);

    public static void Set(Type rootType, XmlSerializer serializer) => Registry.Value.Set(rootType, serializer);
}
