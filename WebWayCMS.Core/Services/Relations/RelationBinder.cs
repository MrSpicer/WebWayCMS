using System.Reflection;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;

namespace WebWayCMS.Services.Relations;

/// <inheritdoc cref="IRelationBinder"/>
public sealed class RelationBinder : IRelationBinder
{
    private readonly IContentRelations _relations;

    public RelationBinder(IContentRelations relations)
    {
        _relations = relations ?? throw new ArgumentNullException(nameof(relations));
    }

    public async Task<AdminSaveResult?> ValidateAsync(object model, string displayName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        var declared = RelationProperties.Describe(model.GetType());
        if (declared.Count == 0)
            return null;

        var values = RelationProperties.Read(model);
        var nodes = await _relations.GetNodesAsync(values.Select(v => v.TargetNodeId), ct);

        foreach (var relation in declared)
        {
            var targets = values.Where(v => v.Name == relation.Name).Select(v => v.TargetNodeId).ToList();
            if (relation.Attribute.Required && targets.Count == 0)
                return Error(relation, displayName, missing: true);

            foreach (var target in targets)
            {
                // A draft target may be chosen: it only has to exist as the declared content type.
                if (!nodes.TryGetValue(target, out var node) || !IsTargetType(node.ContentTypeKey, relation.Attribute.TargetContentType))
                    return Error(relation, displayName, missing: false);
            }
        }

        return null;
    }

    /// <summary>The node's type key is the declared key, or its plural (as the entity picker resolves it).</summary>
    internal static bool IsTargetType(string nodeTypeKey, string declared)
        => string.Equals(nodeTypeKey, declared, StringComparison.OrdinalIgnoreCase)
        || string.Equals(nodeTypeKey, declared + "s", StringComparison.OrdinalIgnoreCase);

    private static AdminSaveResult Error(RelationPropertyInfo relation, string displayName, bool missing)
    {
        var label = relation.Property.GetCustomAttribute<FormPropertyAttribute>()?.Label is { Length: > 0 } l
            ? l
            : FormPropertyBuilder.InsertSpaces(relation.Name);

        var message = relation.Attribute.Kind == ContentRelationKind.Containment
            ? $"Choose the {label.ToLowerInvariant()} this {displayName.ToLowerInvariant()} belongs to."
            : missing
                ? $"{label} is required."
                : $"{label}: the selected item no longer exists.";
        return new AdminSaveResult(false, message, relation.Name);
    }
}
