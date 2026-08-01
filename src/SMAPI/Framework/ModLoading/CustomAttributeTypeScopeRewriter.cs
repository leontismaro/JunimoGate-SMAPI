using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Collections.Generic;

namespace StardewModdingAPI.Framework.ModLoading;

/// <summary>Rewrites type scopes embedded in custom attribute metadata.</summary>
internal static class CustomAttributeTypeScopeRewriter
{
    /// <summary>Rewrite every type reference reachable from custom attributes in a module.</summary>
    /// <param name="module">The module whose attributes should be scanned.</param>
    /// <param name="rewrite">The type-reference rewrite to apply.</param>
    public static void Rewrite(ModuleDefinition module, Action<TypeReference> rewrite)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(rewrite);

        if (module.Assembly is not null)
            Rewrite(module.Assembly.CustomAttributes, rewrite);
        Rewrite(module.CustomAttributes, rewrite);

        foreach (TypeDefinition type in GetTypes(module.Types))
        {
            Rewrite(type.CustomAttributes, rewrite);
            Rewrite(type.GenericParameters, rewrite);

            foreach (FieldDefinition field in type.Fields)
                Rewrite(field.CustomAttributes, rewrite);
            foreach (PropertyDefinition property in type.Properties)
                Rewrite(property.CustomAttributes, rewrite);
            foreach (EventDefinition @event in type.Events)
                Rewrite(@event.CustomAttributes, rewrite);
            foreach (MethodDefinition method in type.Methods)
            {
                Rewrite(method.CustomAttributes, rewrite);
                Rewrite(method.MethodReturnType.CustomAttributes, rewrite);
                Rewrite(method.GenericParameters, rewrite);
                foreach (ParameterDefinition parameter in method.Parameters)
                    Rewrite(parameter.CustomAttributes, rewrite);
            }
        }
    }

    private static IEnumerable<TypeDefinition> GetTypes(Collection<TypeDefinition> types)
    {
        foreach (TypeDefinition type in types)
        {
            yield return type;
            foreach (TypeDefinition nested in GetTypes(type.NestedTypes))
                yield return nested;
        }
    }

    private static void Rewrite(Collection<GenericParameter> parameters, Action<TypeReference> rewrite)
    {
        foreach (GenericParameter parameter in parameters)
            Rewrite(parameter.CustomAttributes, rewrite);
    }

    private static void Rewrite(Collection<CustomAttribute> attributes, Action<TypeReference> rewrite)
    {
        foreach (CustomAttribute attribute in attributes)
        {
            rewrite(attribute.AttributeType);
            foreach (CustomAttributeArgument argument in attribute.ConstructorArguments)
                Rewrite(argument, rewrite);
            foreach (CustomAttributeNamedArgument property in attribute.Properties)
                Rewrite(property.Argument, rewrite);
            foreach (CustomAttributeNamedArgument field in attribute.Fields)
                Rewrite(field.Argument, rewrite);
        }
    }

    private static void Rewrite(CustomAttributeArgument argument, Action<TypeReference> rewrite)
    {
        rewrite(argument.Type);
        switch (argument.Value)
        {
            case TypeReference type:
                rewrite(type);
                break;
            case CustomAttributeArgument[] values:
                foreach (CustomAttributeArgument value in values)
                    Rewrite(value, rewrite);
                break;
        }
    }
}
