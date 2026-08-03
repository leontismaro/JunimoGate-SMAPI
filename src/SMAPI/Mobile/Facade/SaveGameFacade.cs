using System;
using System.IO;
using System.Xml.Serialization;
using StardewModdingAPI.Framework.ModLoading.Rewriters;
using StardewModdingAPI.Mobile.Serialization;
using StardewValley;
using StardewValley.Quests;

namespace StardewModdingAPI.Mobile.Facade;

public class SaveGameFacade : SaveGame, IRewriteFacade
{
    private static XmlSerializer? LegacyDescriptionElementSerializer;

    public static void ensureFolderStructureExists()
    {
        string folderName = FilterFileName(Game1.GetSaveGameName()) + "_" + Game1.uniqueIDForThisGame;
        Directory.CreateDirectory(Path.Combine(StardewValley.Program.GetSavesFolder(), folderName));
    }

    public static XmlSerializer serializer
    {
        get => AndroidSaveSerializerBridge.Get(typeof(SaveGame));
        set => AndroidSaveSerializerBridge.Set(typeof(SaveGame), value);
    }

    public static XmlSerializer farmerSerializer
    {
        get => AndroidSaveSerializerBridge.Get(typeof(Farmer));
        set => AndroidSaveSerializerBridge.Set(typeof(Farmer), value);
    }

    public static XmlSerializer locationSerializer
    {
        get => AndroidSaveSerializerBridge.Get(typeof(GameLocation));
        set => AndroidSaveSerializerBridge.Set(typeof(GameLocation), value);
    }

    public static XmlSerializer descriptionElementSerializer
    {
        get => AndroidSaveSerializerBridge.Get(typeof(DescriptionElement));
        set
        {
            AndroidSaveSerializerBridge.Set(typeof(DescriptionElement), value);
            DescriptionElement.serializer = value;
        }
    }

    public static XmlSerializer legacyDescriptionElementSerializer
    {
        get => LegacyDescriptionElementSerializer ?? AndroidSaveSerializerBridge.Get(typeof(DescriptionElement));
        set => LegacyDescriptionElementSerializer = value ?? throw new ArgumentNullException(nameof(value));
    }
}
