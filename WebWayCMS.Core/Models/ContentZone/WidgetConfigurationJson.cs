using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using WebWayCMS.ContentZones;
using WebWayCMS.Security;

namespace WebWayCMS.Models.ContentZone;

/// <summary>
/// Widget configuration JSON (<c>ComponentPropertiesJson</c>) helpers shared by every zone-item
/// write path (the zone editor API and the child handler behind the admin form, MCP and the seeder)
/// and the render-time deserializer, so save-time sanitization and rendering resolve the same type.
/// </summary>
public static class WidgetConfigurationJson
{
    // Sanitized HTML is stored, never emitted into a <script>; relaxed escaping keeps markup and
    // non-ASCII text readable instead of inflating every '<' to < against the 4000-char column.
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>The registered configuration type for <paramref name="componentName"/>, if it resolves.</summary>
    public static Type? ResolveConfigurationType(IWidgetRegistry registry, string componentName)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return ResolveType(registry.GetByName(componentName)?.ConfigurationTypeName);
    }

    /// <summary>Resolves a configuration type name, searching every loaded assembly.</summary>
    public static Type? ResolveType(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        var type = Type.GetType(typeName, throwOnError: false);
        if (type != null)
            return type;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = asm.GetType(typeName, throwOnError: false);
            if (type != null)
                return type;
        }

        return null;
    }

    /// <summary>
    /// Sanitizes the rich-text properties of a widget configuration payload in place. Only the
    /// values of the configuration type's <c>RichText</c> string properties are rewritten; every
    /// other key (including ones the type does not declare) is kept, and a payload with nothing to
    /// clean is returned verbatim. A component with no resolvable type or no rich-text properties
    /// is returned as-is. A payload that cannot be read as a JSON object is replaced with
    /// <c>"{}"</c> — it would render as the default configuration anyway, and passing it through
    /// unsanitized could let a lenient reader see markup this one did not.
    /// </summary>
    public static string Sanitize(IWidgetRegistry registry, string componentName, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "{}";

        var configType = ResolveConfigurationType(registry, componentName);
        if (configType == null)
            return json;

        var richTextNames = RichTextSanitizer.GetRichTextProperties(configType)
            .Select(JsonNameOf)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (richTextNames.Count == 0)
            return json;

        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
                return "{}";

            var changed = false;
            // Enumerating materializes the object, which is where duplicate keys throw.
            foreach (var (key, value) in root.ToList())
            {
                if (!richTextNames.Contains(key)
                    || value is not JsonValue scalar
                    || !scalar.TryGetValue<string>(out var html))
                    continue;

                var clean = RichTextSanitizer.SanitizeHtml(html);
                if (clean == html)
                    continue;

                root[key] = clean;
                changed = true;
            }

            return changed ? root.ToJsonString(WriteOptions) : json;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return "{}";
        }
    }

    // Matches the deserializer's binding: an explicit [JsonPropertyName] wins over the CLR name.
    private static string JsonNameOf(PropertyInfo prop)
        => prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? prop.Name;
}
