using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

/// <summary>
/// A zone's owner and slot in the shape of the retired <c>ContentZoneAssignments</c> row, so the zone tests
/// keep their arrange blocks: seeding one writes the zone version's <see cref="ZoneRelations.Owner"/> relation.
/// </summary>
internal sealed record ZoneAssignment
{
    public Guid Id { get; init; }
    public string SlotName { get; init; } = string.Empty;
    public Guid ContentZoneNodeId { get; init; }
    public Guid? ParentPageNodeId { get; init; }
    public Guid? ParentItemNodeId { get; init; }
}

/// <summary>Builds zone structure as stored relation rows (what the store would have written).</summary>
internal static class ZoneFixture
{
    /// <summary>Stores <paramref name="version"/>'s placement as a <see cref="ZoneRelations.Zone"/> row.</summary>
    public static void Place(ContentVersion version, Guid zoneNodeId, int ordinal)
    {
        version.Relations.RemoveAll(r => r.Name == ZoneRelations.Zone);
        version.Relations.Add(new ContentRelation
        {
            Id = Guid.NewGuid(),
            SourceVersionId = version.Id,
            TargetNodeId = zoneNodeId,
            Name = ZoneRelations.Zone,
            Ordinal = ordinal,
            Kind = RelationKind.Containment
        });
    }

    /// <summary>A copy of <paramref name="item"/> with a fresh version row id, placed as given (a stored older/newer version).</summary>
    public static ContentZoneItemDTO PlacedCopy(ContentZoneItemDTO item, Guid zoneNodeId, int ordinal)
    {
        var copy = item with { Version = item.Version with { Relations = item.Version.Relations.Select(r => r with { }).ToList() } };
        Place(copy.Version, zoneNodeId, ordinal);
        return copy;
    }

    /// <summary>
    /// Writes each assignment as an Owner relation on its zone's current version. What the assignment
    /// refers to and nobody seeded is created, published, so containment visibility sees a real chain: the
    /// zone itself, a page owner (a <c>GenericPage</c>), or a container-item owner (an unplaced item — the
    /// owner node's type is also what marks it as an item). An assignment with neither parent (an orphan)
    /// writes no relation. Returns the zones it created.
    /// </summary>
    public static async Task<List<Guid>> SeedAsync(CmsDbContext ctx, params ZoneAssignment[] assignments)
    {
        var created = new List<Guid>();
        foreach (var a in assignments)
        {
            var version = await ctx.Set<ContentVersion>()
                .Where(v => v.NodeId == a.ContentZoneNodeId)
                .OrderByDescending(v => v.IsCurrentDraft)
                .ThenByDescending(v => v.VersionNumber)
                .FirstOrDefaultAsync();
            if (version == null)
            {
                var zone = PublishedZone(a.ContentZoneNodeId, a.SlotName);
                ctx.Set<ContentZoneDTO>().Add(zone);
                version = zone.Version;
                created.Add(a.ContentZoneNodeId);
            }

            if (a.ParentPageNodeId is { } pageNodeId && !await NodeExistsAsync(ctx, pageNodeId))
                ctx.Set<PageDTO>().Add(PublishedPage(pageNodeId));

            if (a.ParentItemNodeId is { } itemNodeId && !await NodeExistsAsync(ctx, itemNodeId))
                ctx.Set<ContentZoneItemDTO>().Add(PublishedItem(itemNodeId));

            if ((a.ParentPageNodeId ?? a.ParentItemNodeId) is { } owner)
            {
                ctx.Set<ContentRelation>().Add(new ContentRelation
                {
                    Id = Guid.NewGuid(),
                    SourceVersionId = version.Id,
                    TargetNodeId = owner,
                    Name = ZoneRelations.Owner,
                    Qualifier = a.SlotName,
                    Kind = RelationKind.Containment
                });
            }
            await ctx.SaveChangesAsync();
        }
        return created;
    }

    /// <summary>Seeds a published global zone for each item zone that nobody seeded (returned).</summary>
    public static async Task<List<Guid>> SeedMissingZonesAsync(CmsDbContext ctx, IEnumerable<ContentZoneItemDTO> items)
    {
        var created = new List<Guid>();
        foreach (var zoneNodeId in items.Select(i => i.ContentZoneNodeId).Where(z => z != Guid.Empty).Distinct())
        {
            if (await NodeExistsAsync(ctx, zoneNodeId))
                continue;
            ctx.Set<ContentZoneDTO>().Add(PublishedZone(zoneNodeId, "Zone"));
            created.Add(zoneNodeId);
        }
        await ctx.SaveChangesAsync();
        return created;
    }

    /// <summary>Removes a zone <see cref="SeedAsync"/>/<see cref="SeedMissingZonesAsync"/> created, keeping its Owner rows' owners.</summary>
    public static async Task<List<ContentRelation>> RemoveAutoZoneAsync(CmsDbContext ctx, Guid zoneNodeId)
    {
        var versions = await ctx.Set<ContentVersion>().Where(v => v.NodeId == zoneNodeId).ToListAsync();
        var owners = versions.SelectMany(v => v.Relations).Where(r => r.Name == ZoneRelations.Owner).ToList();
        ctx.Set<ContentZoneDTO>().RemoveRange(await ctx.Set<ContentZoneDTO>().Where(z => z.Version.NodeId == zoneNodeId).ToListAsync());
        ctx.Set<ContentVersion>().RemoveRange(versions);
        ctx.Set<ContentNode>().RemoveRange(await ctx.Set<ContentNode>().Where(n => n.Id == zoneNodeId).ToListAsync());
        await ctx.SaveChangesAsync();
        return owners;
    }

    private static async Task<bool> NodeExistsAsync(CmsDbContext ctx, Guid nodeId)
        => await ctx.Set<ContentNode>().AnyAsync(n => n.Id == nodeId)
        || ctx.ChangeTracker.Entries<ContentNode>().Any(e => e.Entity.Id == nodeId);

    private static ContentVersion PublishedVersion(Guid nodeId, string contentType, string title)
    {
        var versionId = Guid.NewGuid();
        return new ContentVersion
        {
            Id = versionId,
            NodeId = nodeId,
            Node = new ContentNode { Id = nodeId, ContentTypeKey = contentType },
            State = ContentVersionState.Published,
            IsCurrentDraft = true,
            Title = title
        };
    }

    private static ContentZoneDTO PublishedZone(Guid nodeId, string name)
    {
        var version = PublishedVersion(nodeId, "contentzones", name);
        return new ContentZoneDTO { VersionId = version.Id, Name = name, Version = version };
    }

    private static PageDTO PublishedPage(Guid nodeId)
    {
        var version = PublishedVersion(nodeId, "pages", "Page");
        return new PageDTO { VersionId = version.Id, ControllerName = "GenericPage", ConfigurationJson = "{}", Version = version };
    }

    private static ContentZoneItemDTO PublishedItem(Guid nodeId)
    {
        var version = PublishedVersion(nodeId, ZoneRelations.ItemContentType, "Layout");
        return new ContentZoneItemDTO { VersionId = version.Id, ComponentName = "Layout", Version = version };
    }

    /// <summary>The live owner rows, in the old assignment shape (for assertions).</summary>
    public static List<ZoneAssignment> OwnerRows(CmsDbContext ctx)
        => new PageZoneTree(ctx).ZoneOwners()
            .ToList()
            .Select(o => new ZoneAssignment
            {
                SlotName = o.SlotName,
                ContentZoneNodeId = o.ZoneNodeId,
                ParentPageNodeId = o.ParentPageNodeId,
                ParentItemNodeId = o.ParentItemNodeId
            })
            .ToList();

    /// <summary>The ids of the items with any version in <paramref name="zoneNodeId"/> (for assertions).</summary>
    public static IQueryable<ContentZoneItemDTO> ItemsIn(CmsDbContext ctx, Guid zoneNodeId)
        => ctx.Set<ContentZoneItemDTO>().Where(ZoneRelations.InZone(zoneNodeId));
}
