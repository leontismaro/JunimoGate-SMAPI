using Mono.Cecil;

namespace StardewModdingAPI.Framework.ModLoading;

/// <summary>Checks whether a Cecil type is visible to another assembly.</summary>
internal static class TypeDefinitionVisibility
{
    /// <summary>Get whether the type and every declaring type are publicly visible.</summary>
    public static bool IsPubliclyVisible(TypeDefinition type)
    {
        if (!type.IsNested)
            return type.IsPublic;

        return type.IsNestedPublic
            && type.DeclaringType is not null
            && IsPubliclyVisible(type.DeclaringType);
    }
}
