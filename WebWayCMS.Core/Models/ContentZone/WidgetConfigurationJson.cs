using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Forms;
using WebWayCMS.Security;
using WebWayCMS.Services.Relations;

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

    /// <summary>
    /// The stored configuration-JSON column limit (<c>ComponentPropertiesJson</c> on a zone item and
    /// <c>ConfigurationJson</c> on a page). Every write path checks its payload with
    /// <see cref="LengthError"/> first, so an overflow is a validation message rather than a raw
    /// <c>DbUpdateException</c> the seeder would then retry forever.
    /// </summary>
    public const int MaxJsonLength = 4000;

    /// <summary>
    /// The friendly error when <paramref name="json"/> would overflow its column, or <c>null</c>
    /// when it fits. <paramref name="what"/> names the payload in the message (e.g.
    /// <c>"ContentBlock configuration"</c>).
    /// </summary>
    public static string? LengthError(string? json, string what)
        => json != null && json.Length > MaxJsonLength
            ? $"{what} exceeds the {MaxJsonLength} character limit ({json.Length} characters). Shorten the configuration or move long text into a content item."
            : null;

    /// <summary>
    /// Checks a widget configuration's <c>[FormProperty]</c> constraints (required, min, max, max length,
    /// pattern) server-side — the same checks the config form renders client-side, so a zone editor API,
    /// MCP or content-seed write cannot bypass them. Pass the caller's payload <b>before</b>
    /// <see cref="ExtractRelations(IWidgetRegistry, string, string?)"/>: a required <c>[ContentRelation]</c>
    /// id is still in it (stripped, it would read as <see cref="Guid.Empty"/> and fail as missing). Only the
    /// fields of <paramref name="displayMode"/> are checked, as only those are on the item's form; an empty
    /// payload is checked as <c>"{}"</c>, so a widget with a required field cannot skip validation.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        IWidgetRegistry registry, string componentName, string? json, WidgetDisplayMode displayMode)
        => registry.ValidateConfiguration(componentName, string.IsNullOrWhiteSpace(json) ? "{}" : json, displayMode);

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

    /// <summary>
    /// Lifts a widget configuration payload's <c>[ContentRelation]</c> values out of its JSON. The ids
    /// live on the zone item's version as <c>config.*</c> relations, not in the stored configuration.
    /// Returns the JSON with those keys removed and the relation values in declaration order. A payload
    /// with no resolvable type or no relation properties is returned unchanged; an unreadable payload
    /// fails closed to <c>"{}"</c> with no relations, exactly like <see cref="Sanitize"/>.
    /// </summary>
    public static (string StrippedJson, IReadOnlyList<RelationValue> Relations) ExtractRelations(
        Type? configType, string? json)
    {
        if (configType == null)
            return (json ?? "{}", []);

        var properties = RelationProperties.Describe(configType);
        if (properties.Count == 0)
            return (json ?? "{}", []);

        if (string.IsNullOrWhiteSpace(json))
            return ("{}", []);

        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
                return ("{}", []);

            var relations = new List<RelationValue>();
            var changed = false;
            // Enumerating materializes the object, which is where duplicate keys throw.
            foreach (var (key, value) in root.ToList())
            {
                var prop = properties.FirstOrDefault(p =>
                    string.Equals(JsonNameOf(p.Property), key, StringComparison.OrdinalIgnoreCase));
                if (prop == null)
                    continue;

                relations.AddRange(ReadRelationValues(prop, value));
                root.Remove(key);
                changed = true;
            }

            return (changed ? root.ToJsonString(WriteOptions) : json, relations);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return ("{}", []);
        }
    }

    /// <summary><see cref="ExtractRelations(Type?, string?)"/> for a registered widget component.</summary>
    public static (string StrippedJson, IReadOnlyList<RelationValue> Relations) ExtractRelations(
        IWidgetRegistry registry, string componentName, string? json)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return ExtractRelations(ResolveConfigurationType(registry, componentName), json);
    }

    private static IEnumerable<RelationValue> ReadRelationValues(RelationPropertyInfo relation, JsonNode? value)
    {
        var name = ContentRelationRegistry.ConfigPrefix + relation.Name;
        var targets = relation.IsMany ? ReadTargets(value) : ReadTarget(value) is { } one ? [one] : [];
        return targets.Select((target, i) => new RelationValue(name, target, i, string.Empty));
    }

    private static IEnumerable<Guid> ReadTargets(JsonNode? value)
    {
        if (value is not JsonArray array)
            return [];

        return array
            .Select(item => item is JsonValue scalar && scalar.TryGetValue<string>(out var text) && Guid.TryParse(text, out var id)
                ? id
                : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct();
    }

    private static Guid? ReadTarget(JsonNode? value)
        => value is JsonValue scalar && scalar.TryGetValue<string>(out var text) && Guid.TryParse(text, out var id)
            ? id
            : null;

    /// <summary>
    /// Writes a configuration payload's <c>[ContentRelation]</c> values onto the owning version as
    /// <c>config.*</c> rows. Every declared name is written, so an emptied property clears its relation.
    /// </summary>
    public static void ApplyRelations(
        WebWayCMS.Data.Models.ContentVersion version,
        Type? configType,
        IEnumerable<RelationValue> relations)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(relations);
        if (configType == null)
            return;

        foreach (var relation in RelationProperties.Describe(configType))
        {
            var name = ContentRelationRegistry.ConfigPrefix + relation.Name;
            version.SetRelations(
                name,
                relations.Where(v => v.Name == name).OrderBy(v => v.Ordinal).Select(v => v.TargetNodeId),
                relation.Attribute.Kind.ToData());
        }
    }

    /// <summary>
    /// Returns the configuration payload with its relation values hydrated from the owning version's
    /// <c>config.*</c> rows, for the editor, the config form and MCP. The stored JSON has no relation keys.
    /// </summary>
    public static string HydrateRelations(
        Type? configType,
        string? json,
        IEnumerable<WebWayCMS.Data.Models.ContentRelation> relations)
    {
        ArgumentNullException.ThrowIfNull(relations);
        if (configType == null)
            return json ?? "{}";

        var properties = RelationProperties.Describe(configType);
        if (properties.Count == 0)
            return json ?? "{}";

        try
        {
            var root = string.IsNullOrWhiteSpace(json) ? new JsonObject() : JsonNode.Parse(json) as JsonObject;
            if (root == null)
                return "{}";

            var byName = relations.ToLookup(r => r.Name, StringComparer.Ordinal);
            foreach (var relation in properties)
            {
                var key = JsonNameOf(relation.Property);
                root.Remove(key);

                var targets = byName[ContentRelationRegistry.ConfigPrefix + relation.Name]
                    .OrderBy(r => r.Ordinal)
                    .Select(r => r.TargetNodeId)
                    .ToList();
                if (targets.Count == 0)
                    continue;

                root[key] = relation.IsMany
                    ? new JsonArray(targets.Select(t => (JsonNode?)JsonValue.Create(t.ToString())).ToArray())
                    : JsonValue.Create(targets[0].ToString());
            }

            return root.ToJsonString(WriteOptions);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return "{}";
        }
    }
}
