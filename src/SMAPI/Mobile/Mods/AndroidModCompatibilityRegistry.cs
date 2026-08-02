namespace StardewModdingAPI.Mobile.Mods;

internal static class AndroidModCompatibilityRegistry
{
    public static void Initialize()
    {
        var modFix = AndroidModFixManager.Init();
        FarmTypeManagerFix.Init(modFix);
        SpaceCoreFix.Init(modFix);
        SveFix.Init(modFix);
        GenericConfigMenuModFix.Init(modFix);
        UnlockableBundlesModFix.Init(modFix);
        FashionSenseModFix.Init(modFix);
        DisableSaveBackup.Init(modFix);
        ModQuickSaveOptionPage.Init(modFix);
    }
}
