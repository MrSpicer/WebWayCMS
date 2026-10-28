using WebWayCMS.Data.Models;
using WebWayCMS.Forms;

namespace WebWayCMS.Services.Relations;

/// <summary>
/// The mapper's generic relation step, run after every profile mapping so models need no per-type
/// relation code: a view model's <c>[ContentRelation]</c> properties become relation writes on the
/// destination's version, and a version's relations fill a view model's properties.
/// </summary>
internal static class RelationMapping
{
    public static void Apply(object source, object destination)
    {
        if (destination is IVersionedContent dto)
        {
            var relations = RelationProperties.Describe(source.GetType());
            if (relations.Count > 0)
            {
                // Every declared name is written, so an emptied property clears its relation.
                var values = RelationProperties.Read(source);
                foreach (var relation in relations)
                {
                    dto.Version.SetRelations(
                        relation.Name,
                        values.Where(v => v.Name == relation.Name).Select(v => v.TargetNodeId),
                        relation.Attribute.Kind.ToData());
                }
            }
        }

        if (source is IVersionedContent versioned && RelationProperties.Describe(destination.GetType()).Count > 0)
        {
            RelationProperties.Write(destination, versioned.Version.EffectiveRelations()
                .Select(r => new RelationValue(r.Name, r.TargetNodeId, r.Ordinal, r.Qualifier)));
        }
    }
}
