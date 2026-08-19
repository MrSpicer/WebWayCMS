using System.Text.Json;
using System.Text.Json.Nodes;

namespace WebWayCMS.Content;

/// <summary>
/// Overlays JSON field values onto a strongly-typed upsert view model by serializing the base model
/// to a <see cref="JsonObject"/>, applying the supplied fields, then deserializing back — which
/// handles <c>init</c>-only view-model properties and allows partial updates. Shared by the MCP
/// tools and the JSON content seeder.
/// </summary>
public static class ContentFieldMerger
{
    /// <summary>Web-default (camelCase) options used for the serialize/deserialize round-trip.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Produces a new instance of <paramref name="baseModel"/>'s type with the supplied
    /// <paramref name="fields"/> overlaid on top. Returns <c>null</c> when the payload is not a JSON
    /// object (including a string that does not decode to an object), letting the caller decide how
    /// to report that condition.
    /// </summary>
    public static object? TryMerge(object baseModel, JsonElement fields)
        => TryGetObject(fields) is { } overlay ? TryMerge(baseModel, overlay) : null;

    /// <summary>Overlays the properties of <paramref name="overlay"/> onto <paramref name="baseModel"/>.</summary>
    public static object? TryMerge(object baseModel, JsonObject overlay)
    {
        var node = JsonSerializer.SerializeToNode(baseModel, baseModel.GetType(), JsonOptions)!.AsObject();

        foreach (var prop in overlay)
            node[prop.Key] = prop.Value?.DeepClone();

        return JsonSerializer.Deserialize(node, baseModel.GetType(), JsonOptions);
    }

    /// <summary>
    /// Interprets <paramref name="fields"/> as a JSON object, transparently unwrapping a JSON
    /// object that arrived encoded as a JSON string. Returns <c>null</c> for any other shape.
    /// </summary>
    internal static JsonObject? TryGetObject(JsonElement fields)
    {
        // Most clients send a JSON object, but some encode the object as a JSON string (when the
        // schema leaves the parameter untyped); accept both so a valid write never silently no-ops.
        // Anything else is unusable and must be reported by the caller rather than stored unchanged.
        switch (fields.ValueKind)
        {
            case JsonValueKind.Object:
                return JsonNode.Parse(fields.GetRawText()) as JsonObject;
            case JsonValueKind.String:
                var raw = fields.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                    return null;
                try
                {
                    return JsonNode.Parse(raw) as JsonObject;
                }
                catch (JsonException)
                {
                    return null;
                }
            default:
                return null;
        }
    }
}
