namespace WebWayCMS.Services.ContentSeeding;

/// <summary>
/// Names the zone a seeded zone item goes into: exactly one of <see cref="Page"/> or <see cref="Item"/>
/// (each with a <see cref="Slot"/>), or <see cref="Name"/> for a global zone. <see cref="Page"/> and
/// <see cref="Item"/> take a node id or an <c>@seed:{guid}</c> reference. The zone is created when it
/// does not exist yet, exactly as rendering the page would create it.
/// </summary>
public sealed class ContentSeedZoneLocator
{
    /// <summary>The owning page, for a zone a page template renders.</summary>
    public string? Page { get; set; }

    /// <summary>The owning zone item, for a nested zone (e.g. a Layout column).</summary>
    public string? Item { get; set; }

    /// <summary>A global, name-based zone.</summary>
    public string? Name { get; set; }

    /// <summary>The slot name the template renders, e.g. <c>Main</c>. Required with Page or Item.</summary>
    public string? Slot { get; set; }
}
