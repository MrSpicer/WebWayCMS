using System.Text.Json;
using System.Text.Json.Serialization;

using WebWayCMS.Attributes;

namespace WebWayCMS.ContentZones;

/// <summary>
/// Reads <see cref="WidgetMetadata.DisplayMode"/> from a name (case-insensitive) or a number, and
/// writes the name. An empty string or null reads as <see cref="WidgetDisplayMode.Single"/>: the
/// generic dropdown form component always offers an empty "-- Select --" option, and choosing it
/// means "the default", not malformed metadata. Range checks (exactly one mode) are left to
/// <c>WidgetMetadataJson.Normalize</c>.
/// </summary>
public sealed class WidgetDisplayModeJsonConverter : JsonConverter<WidgetDisplayMode>
{
    public override bool HandleNull => true;

    public override WidgetDisplayMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return WidgetDisplayMode.Single;

            case JsonTokenType.Number:
                return (WidgetDisplayMode)reader.GetInt32();

            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text))
                    return WidgetDisplayMode.Single;
                if (Enum.TryParse<WidgetDisplayMode>(text, ignoreCase: true, out var parsed))
                    return parsed;
                throw new JsonException($"'{text}' is not a widget display mode.");

            default:
                throw new JsonException("A widget display mode must be a string or a number.");
        }
    }

    public override void Write(Utf8JsonWriter writer, WidgetDisplayMode value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
