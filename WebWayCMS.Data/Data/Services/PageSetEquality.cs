using System.Security.Cryptography;
using System.Text;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Content comparison for the page-set history: two versions are equal when everything an editor
/// controls matches, regardless of version id or number. Shared by the revert (which skips an item
/// already at the snapshot's content), the draft state token, and the history diff.
/// </summary>
public static class PageSetEquality
{
    /// <summary>The page fields a revert restores and a comparison inspects.</summary>
    public static bool PagesEqual(PageDTO a, PageDTO b)
        => PageFingerprint(a) == PageFingerprint(b);

    /// <summary>Equal content and placement (zone + ordinal) and active flag.</summary>
    public static bool ItemsEqual(ContentZoneItemDTO a, ContentZoneItemDTO b)
        => ItemContentEqual(a, b)
        && a.IsActive == b.IsActive
        && a.ContentZoneNodeId == b.ContentZoneNodeId
        && a.Ordinal == b.Ordinal;

    /// <summary>Equal widget, configuration, metadata and relations; placement and active flag are ignored.</summary>
    public static bool ItemContentEqual(ContentZoneItemDTO a, ContentZoneItemDTO b)
        => a.ComponentName == b.ComponentName
        && a.ComponentPropertiesJson == b.ComponentPropertiesJson
        && Metadata(a.MetadataJson) == Metadata(b.MetadataJson)
        && ContentRelations(a) == ContentRelations(b);

    internal static string PageFingerprint(PageDTO page)
        => WithRelations(page.Version.RelationFingerprint(), string.Join('\u001f',
            page.ControllerName,
            page.ViewName ?? string.Empty,
            page.ConfigurationJson,
            page.Version.Title,
            page.Version.Slug,
            page.Version.PublishStartUtc?.Ticks.ToString() ?? string.Empty,
            page.Version.PublishEndUtc?.Ticks.ToString() ?? string.Empty,
            string.Join('\u001e', page.Version.CustomFields.Select(c => System.Text.Json.JsonSerializer.Serialize(c)))));

    internal static string ItemFingerprint(ContentZoneItemDTO item)
        => WithRelations(ContentRelations(item), string.Join('\u001f',
            item.ComponentName,
            item.ComponentPropertiesJson,
            Metadata(item.MetadataJson),
            item.IsActive ? "1" : "0",
            item.ContentZoneNodeId.ToString(),
            item.Ordinal.ToString()));

    /// <summary>
    /// An item's relations other than its placement: the <see cref="ZoneRelations.Zone"/> relation is the
    /// zone and ordinal, which the fingerprint carries as fields and content equality ignores.
    /// </summary>
    private static string ContentRelations(ContentZoneItemDTO item)
        => item.Version.RelationFingerprint(ZoneRelations.Zone);

    /// <summary>
    /// Appends relations to a fingerprint. Nothing is appended when there are none, so content without
    /// relations fingerprints (and hashes) exactly as it did before relations existed.
    /// </summary>
    private static string WithRelations(string relations, string fingerprint)
        => relations.Length > 0
            ? fingerprint + '\u001f' + relations
            : fingerprint;

    /// <summary>
    /// A token over every current draft of a page set (the page and its zone-tree items): their version
    /// numbers and content. Every save mints a version, so any write by anyone changes it.
    /// </summary>
    public static string StateToken(PageDTO page, IEnumerable<ContentZoneItemDTO> items)
        => Hash(items
            .Select(i => $"{i.Version.NodeId}|{i.Version.VersionNumber}|{ItemFingerprint(i)}")
            .OrderBy(l => l, StringComparer.Ordinal)
            .Prepend($"{page.Version.NodeId}|{page.Version.VersionNumber}|{PageFingerprint(page)}"));

    /// <summary>
    /// A hash of a page set's content only — no version ids or numbers — so two states that look the same
    /// hash the same. The page history uses it to skip an entry identical to the previous one.
    /// </summary>
    public static string ContentHash(PageDTO page, IEnumerable<ContentZoneItemDTO> items)
        => Hash(items
            .Select(i => $"{i.Version.NodeId}|{ItemFingerprint(i)}")
            .OrderBy(l => l, StringComparer.Ordinal)
            .Prepend(PageFingerprint(page)));

    private static string Hash(IEnumerable<string> lines)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));

    private static string Metadata(string? json) => string.IsNullOrWhiteSpace(json) ? "{}" : json;
}
