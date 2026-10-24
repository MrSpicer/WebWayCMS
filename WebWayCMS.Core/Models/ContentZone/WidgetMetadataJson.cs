using System.Text.Json;
using System.Text.Json.Serialization;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Security;

namespace WebWayCMS.Models.ContentZone;

/// <summary>
/// Reads and writes a zone item's <c>MetadataJson</c> column. Every write path (the zone editor's
/// metadata dialog and the MCP/content-seed child saves) goes through <see cref="Normalize"/>, so
/// stored metadata is always a validated <see cref="WidgetMetadata"/> re-serialized by the CMS —
/// never caller-supplied JSON verbatim.
/// </summary>
public static class WidgetMetadataJson
{
    /// <summary>Column length cap (matches the EF configuration).</summary>
    public const int MaxLength = 4000;

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// True when stored metadata selects <see cref="WidgetDisplayMode.List"/> (whether or not the
    /// widget still supports it).
    /// </summary>
    public static bool StoresList(string? json) => Parse(json).DisplayMode == WidgetDisplayMode.List;

    /// <summary>
    /// A historical version's metadata as it can be saved back today (an item restore or a page-history
    /// revert). A historical List on a widget that has since dropped list support (and that the current
    /// item does not already store as List) is restored as Single — it renders as Single anyway, and
    /// saving List would be rejected.
    /// </summary>
    public static string? Restorable(string? historicalJson, WidgetRegistrationInfo? widget, string? currentJson)
    {
        if (!StoresList(historicalJson)
            || widget?.SupportsList == true
            || StoresList(currentJson))
            return historicalJson;

        var metadata = Parse(historicalJson);
        metadata.DisplayMode = WidgetDisplayMode.Single;
        return JsonSerializer.Serialize(metadata);
    }

    /// <summary>
    /// Parses stored metadata for rendering. Missing or malformed JSON degrades to an empty
    /// <see cref="WidgetMetadata"/> rather than dropping the item.
    /// </summary>
    public static WidgetMetadata Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new WidgetMetadata();

        try
        {
            return JsonSerializer.Deserialize<WidgetMetadata>(json, ReadOptions) ?? new WidgetMetadata();
        }
        catch (JsonException)
        {
            return new WidgetMetadata();
        }
    }

    /// <summary>
    /// Validates caller-supplied metadata JSON and returns the canonical form to store. Returns a
    /// failure result (and a null <paramref name="normalized"/>) when the JSON is not a
    /// <see cref="WidgetMetadata"/> object, a field fails validation (e.g. the CSS class / anchor id
    /// patterns), the display mode is <see cref="WidgetDisplayMode.List"/> for a widget that does not
    /// support lists (<paramref name="component"/> is null or has <c>SupportsList == false</c>), or
    /// the canonical form exceeds <see cref="MaxLength"/>; otherwise <c>null</c>.
    /// </summary>
    public static AdminSaveResult? Normalize(string json, WidgetRegistrationInfo? component, out string? normalized)
        => Normalize(json, component, listAlreadyStored: false, MaxLength, out normalized);

    /// <summary>
    /// <see cref="Normalize(string, WidgetRegistrationInfo?, out string?)"/> for an update of an
    /// existing item. When <paramref name="listAlreadyStored"/> is true (the item already stores
    /// <see cref="WidgetDisplayMode.List"/>), List is accepted even though the widget no longer
    /// supports lists: the capability was removed after the item was saved, and rejecting it would
    /// block every later edit of the item. Such an item still renders as Single. Newly choosing List
    /// is still rejected. An incoming document that omits <c>DisplayMode</c> entirely (the settings
    /// dialog of a widget that no longer supports lists hides the field, and it only serializes the
    /// rendered fields) keeps the stored List rather than downgrading to Single.
    /// </summary>
    public static AdminSaveResult? Normalize(string json, WidgetRegistrationInfo? component, bool listAlreadyStored, out string? normalized)
        => Normalize(json, component, listAlreadyStored, MaxLength, out normalized);

    /// <summary>
    /// <see cref="Normalize(string, WidgetRegistrationInfo?, out string?)"/> with an explicit length cap.
    /// </summary>
    internal static AdminSaveResult? Normalize(string json, WidgetRegistrationInfo? component, int maxLength, out string? normalized)
        => Normalize(json, component, listAlreadyStored: false, maxLength, out normalized);

    /// <summary>
    /// True when the caller's JSON carries a display mode explicitly (in any casing, matching the
    /// case-insensitive deserialization), as opposed to omitting the field because its editor hid it.
    /// Only called after the JSON deserialized as a <see cref="WidgetMetadata"/>, so the document is
    /// always a JSON object here.
    /// </summary>
    private static bool HasDisplayModeProperty(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .Any(p => string.Equals(p.Name, nameof(WidgetMetadata.DisplayMode), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <see cref="Normalize(string, WidgetRegistrationInfo?, bool, out string?)"/> with an explicit
    /// length cap. Every <see cref="WidgetMetadata"/> field is individually length-capped, so today's
    /// fields cannot overflow the column even fully escaped; the cap stays as a backstop for future
    /// fields and is injectable so the guard can be exercised.
    /// </summary>
    internal static AdminSaveResult? Normalize(string json, WidgetRegistrationInfo? component, bool listAlreadyStored, int maxLength, out string? normalized)
    {
        normalized = null;

        WidgetMetadata? metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<WidgetMetadata>(json, ReadOptions);
        }
        catch (JsonException)
        {
            metadata = null;
        }

        if (metadata == null)
            return new AdminSaveResult(false, "Metadata must be a JSON object.", "MetadataJson");

        // An editor that hides the display-mode field serializes only the rendered fields, so an
        // omitted mode must not mean "Single": a stored List is carried through unless the caller
        // explicitly sends a mode, or saving the label would silently downgrade the item.
        if (listAlreadyStored && metadata.DisplayMode == WidgetDisplayMode.Single && !HasDisplayModeProperty(json))
            metadata.DisplayMode = WidgetDisplayMode.List;

        var validation = ModelValidator.Validate(metadata);
        if (validation != null)
            return validation;

        if (metadata.DisplayMode != WidgetDisplayMode.Single && metadata.DisplayMode != WidgetDisplayMode.List)
            return new AdminSaveResult(false, "Display mode must be Single or List.", nameof(WidgetMetadata.DisplayMode));

        if (metadata.DisplayMode == WidgetDisplayMode.List && component?.SupportsList != true && !listAlreadyStored)
            return new AdminSaveResult(false, $"Widget '{component?.Name}' does not support list mode.", nameof(WidgetMetadata.DisplayMode));

        var serialized = JsonSerializer.Serialize(metadata, WriteOptions);
        if (serialized.Length > maxLength)
            return new AdminSaveResult(false, $"Metadata must be at most {maxLength} characters.", "MetadataJson");

        normalized = serialized;
        return null;
    }
}
