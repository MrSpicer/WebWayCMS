using System.Text.Json;

namespace WebWayCMS.Forms;

/// <summary>
/// The stored form of a configuration type's fields (a widget's or page type's <c>PropertyDefinitionsJson</c>):
/// <see cref="FormPropertyBuilder.BuildPropertyInfos"/> serialized as a JSON array. Built by the registration
/// admin and the startup seeders, read back by the runtime registries.
/// </summary>
public static class PropertyDefinitionsJson
{
    /// <summary>The empty definition list.</summary>
    public const string Empty = "[]";

    /// <summary>The definitions of <paramref name="configurationType"/>. Throws when the type's fields can't be described.</summary>
    public static string Build(Type configurationType)
    {
        ArgumentNullException.ThrowIfNull(configurationType);
        return JsonSerializer.Serialize(FormPropertyBuilder.BuildPropertyInfos(configurationType));
    }

    /// <summary>
    /// The definitions of the type <paramref name="configurationTypeName"/> names (<see cref="Empty"/> for no
    /// type), or <see cref="Empty"/> and an error when it doesn't resolve or can't be described.
    /// </summary>
    public static (string Json, string? Error) TryBuild(string? configurationTypeName)
    {
        if (string.IsNullOrWhiteSpace(configurationTypeName))
            return (Empty, null);

        var type = ClrTypeResolver.Resolve(configurationTypeName);
        if (type == null)
            return (Empty, $"Configuration type '{configurationTypeName}' could not be resolved.");

        try
        {
            return (Build(type), null);
        }
        catch (Exception ex)
        {
            return (Empty, $"Failed to build properties for type '{configurationTypeName}': {ex.Message}");
        }
    }

    /// <summary>The definitions stored in <paramref name="json"/>; empty when there are none or it is unreadable.</summary>
    public static List<FormPropertyInfo> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == Empty)
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<FormPropertyInfo>>(json, CmsJson.CaseInsensitive) ?? [];
        }
        catch
        {
            // A stored value that no longer reads (hand-edited, or from an older shape) renders no fields.
            return [];
        }
    }
}
