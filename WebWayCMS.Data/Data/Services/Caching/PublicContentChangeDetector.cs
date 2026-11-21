using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services.Caching;

/// <summary>
/// Decides whether a set of pending <see cref="EntityEntry"/>s changes anything a visitor can see.
/// Content is visible only through its published version (or a live, non-versioned row), so:
/// a version that was, is, or becomes <see cref="ContentVersionState.Published"/> with a real
/// column change (flipping only <see cref="ContentVersion.IsCurrentDraft"/> — minting a draft on
/// a live item — changes nothing visitors see); any
/// <see cref="ContentNode"/> change (IsHidden/IsDeleted/IsArchived are live at once); any
/// <see cref="CMSRouteDTO"/> change (the live projection table); and a <see cref="ContentRelation"/>
/// added to or removed from such a version. Drafts, change sets, page-set snapshots, the seed
/// ledger, media blobs, Identity/user rows, API tokens and form-component registrations (admin-only
/// rendering) never bump the generation.
/// </summary>
public static class PublicContentChangeDetector
{
    /// <summary>True when any entry in the set is a public change (see the type summary for the rules).</summary>
    public static bool IsPublicChange(IEnumerable<EntityEntry> entries)
    {
        var versionPublicInSave = new Dictionary<Guid, bool>();
        var versionLivePublished = new Dictionary<Guid, bool>();
        foreach (var entry in entries)
        {
            if (entry.Entity is not ContentVersion version)
                continue;
            versionPublicInSave[version.Id] = VersionWasOrIsPublished(entry);
            versionLivePublished[version.Id] = VersionIsLivePublished(entry);
        }

        foreach (var entry in entries)
        {
            switch (entry.Entity)
            {
                case ContentVersion version when versionPublicInSave[version.Id]:
                    return true;
                case ContentNode when entry.State is EntityState.Modified or EntityState.Deleted:
                    return true;
                case CMSRouteDTO when entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted:
                    return true;
                case ContentRelation relation
                    when entry.State is EntityState.Added or EntityState.Deleted
                        && (versionPublicInSave.GetValueOrDefault(relation.SourceVersionId)
                            || versionLivePublished.GetValueOrDefault(relation.SourceVersionId)):
                    return true;
                case IVersionedContent content
                    when entry.State is EntityState.Modified or EntityState.Deleted
                        && ContentIsPublic(content, versionLivePublished):
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Whether a version was published before the save, is published after it, or both: that is the
    /// whole of "visitors see it". An Unchanged version is not part of the save at all, so a save
    /// that merely tracks published content (without writing it) does not bump the generation on
    /// its own. A modified published version counts only when something besides the
    /// <see cref="ContentVersion.IsCurrentDraft"/> bookkeeping flag changed: minting a draft on a
    /// live item flips the flag, but visitors still see the same published content.
    /// </summary>
    private static bool VersionWasOrIsPublished(EntityEntry entry)
    {
        var current = (ContentVersionState)entry.CurrentValues["State"];
        return entry.State switch
        {
            EntityState.Added => current == ContentVersionState.Published,
            EntityState.Modified => (current == ContentVersionState.Published
                                     || (ContentVersionState)entry.OriginalValues["State"] == ContentVersionState.Published)
                                    && VersionContentChanged(entry),
            EntityState.Deleted => (ContentVersionState)entry.OriginalValues["State"] == ContentVersionState.Published,
            _ => false,
        };
    }

    /// <summary>
    /// True when a modified version's save changes any column but <see cref="ContentVersion.IsCurrentDraft"/>.
    /// </summary>
    private static bool VersionContentChanged(EntityEntry entry)
    {
        foreach (var property in entry.Metadata.GetProperties())
        {
            if (property.Name == nameof(ContentVersion.IsCurrentDraft))
                continue;
            if (entry.Property(property.Name).IsModified)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Whether the version is published in the database as far as the tracked values show —
    /// including an Unchanged version the save does not write: a relation removed from, or a row
    /// modified under, a live version is a public change even though the version row itself is
    /// untouched.
    /// </summary>
    private static bool VersionIsLivePublished(EntityEntry entry)
    {
        var state = entry.State == EntityState.Deleted
            ? (ContentVersionState)entry.OriginalValues["State"]
            : (ContentVersionState)entry.CurrentValues["State"];
        return state == ContentVersionState.Published;
    }

    /// <summary>
    /// Whether a content row's version is published in the database as far as the tracked values
    /// show. If the same save also writes the version row, the version rule has already fired —
    /// for any entry order, the loop returns true on the version entry itself.
    /// </summary>
    private static bool ContentIsPublic(IVersionedContent content, IReadOnlyDictionary<Guid, bool> versionLivePublished)
        => versionLivePublished.TryGetValue(content.VersionId, out var live) && live;
}
