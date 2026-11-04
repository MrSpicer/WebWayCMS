using WebWayCMS.Attributes;
using WebWayCMS.Forms;

namespace WebWayCMS.Services.Relations;

/// <summary>A <c>[ContentRelation]</c> declared by a content type's upsert view model or a configuration type.</summary>
public sealed record ContentRelationDeclaration(
    string SourceContentType,
    Type ModelType,
    RelationPropertyInfo Property,
    string? RelationName = null)
{
    public string Name => RelationName ?? Property.Name;

    public ContentRelationAttribute Attribute => Property.Attribute;
}
