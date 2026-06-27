using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

using ModelContextProtocol;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Mcp;

/// <summary>
/// Shared helpers for the MCP toolsets: handler resolution that mirrors
/// <c>AdminContentController</c>'s guards, JSON field binding, and reflection over
/// <c>[FormProperty]</c> metadata.
/// </summary>
internal static class McpToolHelpers
{
    /// <summary>Web-default (camelCase) options used for binding and describing fields.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>An empty query collection for handler calls that take request query state.</summary>
    public static IQueryCollection EmptyQuery { get; } =
        new QueryCollection(new Dictionary<string, StringValues>());

    /// <summary>Resolves a content handler or throws a protocol error if none is registered.</summary>
    public static IAdminCrudHandler ResolveHandler(IAdminHandlerRegistry registry, string contentType)
    {
        var handler = registry.GetHandler(contentType);
        if (handler == null)
            throw new McpException($"No content type '{contentType}' is registered.");
        return handler;
    }

    /// <summary>
    /// Resolves the child handler for a content type, validating the child type matches
    /// (same checks <c>AdminContentController</c> performs).
    /// </summary>
    public static IAdminCrudChildHandler ResolveChild(IAdminCrudHandler handler, string childType)
    {
        var child = handler.ChildHandler;
        if (child == null || !string.Equals(child.ChildType, childType, StringComparison.OrdinalIgnoreCase))
            throw new McpException(
                $"Content type '{handler.ContentType}' has no child type '{childType}'.");
        return child;
    }

    /// <summary>Extracts the payload value from a handler registry <see cref="IActionResult"/>.</summary>
    public static object? Unwrap(IActionResult result) => result switch
    {
        JsonResult jr => jr.Value,
        ObjectResult or => or.Value,
        _ => null,
    };

    /// <summary>
    /// Produces a new instance of <paramref name="baseModel"/>'s type with the supplied
    /// <paramref name="fields"/> overlaid on top. Serializing then deserializing handles
    /// <c>init</c>-only view-model properties and allows partial updates.
    /// </summary>
    public static object Merge(object baseModel, JsonElement fields)
    {
        var node = JsonSerializer.SerializeToNode(baseModel, baseModel.GetType(), JsonOptions)!.AsObject();

        // Overlay the caller-supplied fields. Most clients send a JSON object, but some encode the
        // object as a JSON string (when the schema leaves the parameter untyped); accept both so a
        // write never silently no-ops. Anything that isn't an object leaves the base model unchanged.
        if (AsObject(fields) is { } overlay)
        {
            foreach (var prop in overlay)
                node[prop.Key] = prop.Value?.DeepClone();
        }

        return JsonSerializer.Deserialize(node, baseModel.GetType(), JsonOptions)!;
    }

    /// <summary>
    /// Interprets <paramref name="fields"/> as a JSON object, transparently unwrapping a JSON
    /// object that arrived encoded as a JSON string. Returns <c>null</c> for any other shape.
    /// </summary>
    private static JsonObject? AsObject(JsonElement fields)
    {
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

    /// <summary>
    /// Reflects the editable, non-hidden <c>[FormProperty]</c> fields of a content type's upsert
    /// view model, using camelCase names that match <see cref="Merge"/>'s binding contract.
    /// </summary>
    public static IReadOnlyList<McpFieldInfo> DescribeFields(object upsertViewModel)
    {
        var fields = new List<McpFieldInfo>();

        foreach (var prop in upsertViewModel.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var form = prop.GetCustomAttribute<FormPropertyAttribute>();
            if (form == null || form.EditorType == EditorType.Hidden)
                continue;

            var required = form.IsRequired || prop.GetCustomAttribute<System.ComponentModel.DataAnnotations.RequiredAttribute>() != null;

            fields.Add(new McpFieldInfo(
                Name: JsonNamingPolicy.CamelCase.ConvertName(prop.Name),
                Label: string.IsNullOrEmpty(form.Label) ? prop.Name : form.Label,
                EditorType: form.EditorType.ToString(),
                Required: required,
                HelpText: string.IsNullOrEmpty(form.HelpText) ? null : form.HelpText,
                Order: form.Order));
        }

        return fields.OrderBy(f => f.Order).ToList();
    }
}