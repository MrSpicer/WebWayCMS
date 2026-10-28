using System.Reflection;
using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

using ModelContextProtocol;

using WebWayCMS.Attributes;
using WebWayCMS.Content;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Forms;

namespace WebWayCMS.Mcp;

/// <summary>
/// Shared helpers for the MCP toolsets: handler resolution that mirrors
/// <c>AdminContentController</c>'s guards, JSON field binding, and reflection over
/// <c>[FormProperty]</c> metadata.
/// </summary>
internal static class McpToolHelpers
{
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

    /// <summary>
    /// A version delete's outcome: deleted, or not deleted for the ordinary reason (the latest version is
    /// always kept), as <c>deleted: false</c>; any other refusal (e.g. the version is part of page
    /// history) is a protocol error carrying the reason.
    /// </summary>
    public static McpDeleteResult DeleteResult((bool Success, string? ErrorMessage) result)
    {
        if (!result.Success && result.ErrorMessage != IAdminCrudHandler.VersionNotDeletedMessage)
            throw new McpException(result.ErrorMessage ?? "The version could not be deleted.");
        return new McpDeleteResult(result.Success);
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
        return ContentFieldMerger.TryMerge(baseModel, fields)
            ?? throw new McpException("fields must be a JSON object");
    }

    /// <summary>
    /// Reflects the editable, non-hidden <c>[FormProperty]</c> and <c>[ContentRelation]</c> fields of a
    /// content type's upsert view model, using camelCase names that match <see cref="Merge"/>'s binding
    /// contract. A relation field carries its <see cref="McpRelationDescription"/>.
    /// </summary>
    public static IReadOnlyList<McpFieldInfo> DescribeFields(object upsertViewModel)
    {
        var fields = new List<McpFieldInfo>();
        var relations = RelationProperties.Describe(upsertViewModel.GetType());

        foreach (var prop in upsertViewModel.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // A [ContentRelation] is a form field on its own (it implies the entity picker).
            var form = prop.GetCustomAttribute<FormPropertyAttribute>();
            var relation = relations.FirstOrDefault(r => r.Property.Name == prop.Name);
            if ((form == null && relation == null) || form?.EditorType == EditorType.Hidden)
                continue;

            var required = form?.IsRequired == true
                || relation?.Attribute.Required == true
                || prop.GetCustomAttribute<System.ComponentModel.DataAnnotations.RequiredAttribute>() != null;

            fields.Add(new McpFieldInfo(
                Name: JsonNamingPolicy.CamelCase.ConvertName(prop.Name),
                Label: string.IsNullOrEmpty(form?.Label) ? prop.Name : form.Label,
                EditorType: relation != null && (form == null || form.EditorType == EditorType.Text)
                    ? nameof(EditorType.Guid)
                    : form!.EditorType.ToString(),
                Required: required,
                HelpText: string.IsNullOrEmpty(form?.HelpText) ? null : form.HelpText,
                Order: form?.Order ?? 0,
                Relation: relation == null
                    ? null
                    : new McpRelationDescription(
                        JsonNamingPolicy.CamelCase.ConvertName(relation.Attribute.Kind.ToString()),
                        relation.Attribute.TargetContentType,
                        relation.IsMany,
                        relation.Attribute.Required)));
        }

        return fields.OrderBy(f => f.Order).ToList();
    }
}