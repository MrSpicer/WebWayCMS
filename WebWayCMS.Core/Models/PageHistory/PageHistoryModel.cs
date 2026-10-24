using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.ContentZone;

namespace WebWayCMS.Models.PageHistory;

/// <summary>
/// The page editor's page-set history: list a page's publish snapshots, inspect one (against the entry
/// before it, or against the current drafts), revert the page to one as drafts, or delete one. Used by
/// the admin page-history screens and the MCP page-history tools.
/// </summary>
public interface IPageHistoryModel
{
    /// <summary>The page's entries, newest first; null when the page does not exist.</summary>
    Task<PageHistoryIndexViewModel?> GetIndexAsync(Guid pageNodeId, CancellationToken ct = default);

    /// <summary>An entry and what it changed; null when the page or entry does not exist (or do not match).</summary>
    Task<PageHistoryEntryViewModel?> GetEntryAsync(Guid pageNodeId, Guid entryId, CancellationToken ct = default);

    /// <summary>The current drafts compared with an entry, plus the token a revert must present.</summary>
    Task<PageHistoryCompareViewModel?> GetCompareAsync(Guid pageNodeId, Guid entryId, CancellationToken ct = default);

    /// <summary>Reverts the page set to the entry as drafts (see <see cref="IPageHistoryService.RevertAsync"/>).</summary>
    Task<(bool Success, string? ErrorMessage)> RevertAsync(Guid pageNodeId, Guid entryId, string stateToken, CancellationToken ct = default);

    /// <summary>Deletes the entry record (its versions are kept); false when it does not exist or belong to the page.</summary>
    Task<bool> DeleteAsync(Guid pageNodeId, Guid entryId, CancellationToken ct = default);
}

/// <inheritdoc cref="IPageHistoryModel"/>
public sealed class PageHistoryModel : IPageHistoryModel
{
    /// <summary>Shown for a zone that no longer exists.</summary>
    public const string MissingZoneName = "(removed zone)";

    private readonly IPageHistoryService _history;
    private readonly IContentStore<PageDTO> _pageStore;
    private readonly IContentStore<ContentZoneDTO> _zoneStore;
    private readonly IWidgetRegistry _registry;

    public PageHistoryModel(
        IPageHistoryService history,
        IContentStore<PageDTO> pageStore,
        IContentStore<ContentZoneDTO> zoneStore,
        IWidgetRegistry registry)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _pageStore = pageStore ?? throw new ArgumentNullException(nameof(pageStore));
        _zoneStore = zoneStore ?? throw new ArgumentNullException(nameof(zoneStore));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<PageHistoryIndexViewModel?> GetIndexAsync(Guid pageNodeId, CancellationToken ct = default)
    {
        var page = await _pageStore.GetCurrentDraftAsync(pageNodeId, ct);
        if (page == null)
            return null;

        var entries = await _history.ListAsync(pageNodeId, ct);
        return new PageHistoryIndexViewModel
        {
            PageNodeId = pageNodeId,
            PageTitle = page.Version.Title,
            Entries = entries
                .Select((entry, i) => Summarize(entry, i + 1 < entries.Count ? entries[i + 1] : null, isLatest: i == 0))
                .ToList()
        };
    }

    public async Task<PageHistoryEntryViewModel?> GetEntryAsync(Guid pageNodeId, Guid entryId, CancellationToken ct = default)
    {
        var loaded = await LoadAsync(pageNodeId, entryId, ct);
        if (loaded == null)
            return null;

        var (page, entry, state) = loaded.Value;
        var previous = await _history.GetPreviousAsync(entry, ct);
        var previousState = previous == null ? null : await _history.GetSnapshotStateAsync(previous.Id, ct);
        var diff = PageSetComparer.Compare(previousState, state);
        var latestId = await _history.GetLatestIdAsync(pageNodeId, ct);

        return new PageHistoryEntryViewModel
        {
            PageNodeId = pageNodeId,
            PageTitle = page.Version.Title,
            Entry = Summarize(entry, previous, isLatest: latestId == entry.Id),
            CapturedPageTitle = state.Page.Version.Title,
            PageChanged = diff.PageChanged,
            PreviousEntryId = previous?.Id,
            Items = await ToRowsAsync(diff, ct)
        };
    }

    public async Task<PageHistoryCompareViewModel?> GetCompareAsync(Guid pageNodeId, Guid entryId, CancellationToken ct = default)
    {
        var loaded = await LoadAsync(pageNodeId, entryId, ct);
        if (loaded == null)
            return null;

        var (page, entry, state) = loaded.Value;
        var current = await _history.GetCurrentStateAsync(pageNodeId, ct);
        if (current == null)
            return null;

        var diff = PageSetComparer.Compare(state, current);
        var latestId = await _history.GetLatestIdAsync(pageNodeId, ct);

        return new PageHistoryCompareViewModel
        {
            PageNodeId = pageNodeId,
            PageTitle = page.Version.Title,
            Entry = Summarize(entry, await _history.GetPreviousAsync(entry, ct), isLatest: latestId == entry.Id),
            CapturedPageTitle = state.Page.Version.Title,
            PageChanged = diff.PageChanged,
            Items = await ToRowsAsync(diff, ct),
            StateToken = PageSetEquality.StateToken(current.Page, current.Items)
        };
    }

    public async Task<(bool Success, string? ErrorMessage)> RevertAsync(Guid pageNodeId, Guid entryId, string stateToken, CancellationToken ct = default)
    {
        var entry = await _history.GetAsync(entryId, ct);
        if (entry == null || entry.PageNodeId != pageNodeId)
            return (false, IPageHistoryService.NotFoundMessage);

        return await _history.RevertAsync(entryId, stateToken,
            (component, historical, current) => WidgetMetadataJson.Restorable(historical, _registry.GetByName(component), current), ct);
    }

    public async Task<bool> DeleteAsync(Guid pageNodeId, Guid entryId, CancellationToken ct = default)
    {
        var entry = await _history.GetAsync(entryId, ct);
        return entry != null && entry.PageNodeId == pageNodeId && await _history.DeleteAsync(entryId, ct);
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private async Task<(PageDTO Page, PageSetSnapshot Entry, PageSetState State)?> LoadAsync(Guid pageNodeId, Guid entryId, CancellationToken ct)
    {
        var page = await _pageStore.GetCurrentDraftAsync(pageNodeId, ct);
        var entry = await _history.GetAsync(entryId, ct);
        if (page == null || entry == null || entry.PageNodeId != pageNodeId)
            return null;

        // A captured version that no longer exists (a broken invariant) reads as a missing entry, not a crash.
        var state = await _history.GetSnapshotStateAsync(entryId, ct);
        return state == null ? null : (page, entry, state);
    }

    /// <summary>
    /// Counts from the stored rows (cheap, no content loads): a widget in a different zone or position is
    /// moved, one at a different version in the same place is updated. The detail view compares content.
    /// </summary>
    private static PageHistoryEntrySummary Summarize(PageSetSnapshot entry, PageSetSnapshot? previous, bool isLatest)
    {
        var before = (previous?.Items ?? []).ToDictionary(i => i.ItemNodeId);
        var after = entry.Items.ToDictionary(i => i.ItemNodeId);
        var kept = after.Values.Where(i => before.ContainsKey(i.ItemNodeId)).ToList();
        bool SamePlace(PageSetSnapshotItem i) => before[i.ItemNodeId].ZoneNodeId == i.ZoneNodeId && before[i.ItemNodeId].Ordinal == i.Ordinal;
        return new PageHistoryEntrySummary
        {
            Id = entry.Id,
            CreatedUtc = entry.CreatedUtc,
            CreatedBy = entry.CreatedBy,
            ItemCount = entry.ItemCount,
            Kind = entry.Kind,
            Note = entry.Note,
            Added = after.Keys.Count(k => !before.ContainsKey(k)),
            Removed = before.Keys.Count(k => !after.ContainsKey(k)),
            Moved = kept.Count(i => !SamePlace(i)),
            Updated = kept.Count(i => SamePlace(i) && before[i.ItemNodeId].ItemVersionId != i.ItemVersionId),
            PageUpdated = previous != null && previous.PageVersionId != entry.PageVersionId,
            IsLatest = isLatest
        };
    }

    private async Task<List<PageHistoryItemRow>> ToRowsAsync(PageSetDiff diff, CancellationToken ct)
    {
        var zoneNames = new Dictionary<Guid, string>();
        foreach (var zoneId in diff.Items.Select(d => d.Item.ContentZoneNodeId).Distinct())
        {
            var zone = await _zoneStore.GetCurrentDraftAsync(zoneId, ct);
            zoneNames[zoneId] = zone?.Name ?? MissingZoneName;
        }

        return diff.Items
            .Select(d => new PageHistoryItemRow
            {
                ItemNodeId = d.Item.Version.NodeId,
                ZoneNodeId = d.Item.ContentZoneNodeId,
                ZoneName = zoneNames[d.Item.ContentZoneNodeId],
                ComponentName = d.Item.ComponentName,
                Label = WidgetMetadataJson.Parse(d.Item.MetadataJson).AdminLabel is { Length: > 0 } label ? label : d.Item.ComponentName,
                Ordinal = d.Item.Ordinal,
                VersionNumber = d.Item.Version.VersionNumber,
                Change = d.Change
            })
            .OrderBy(r => r.ZoneName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ZoneNodeId)
            .ThenBy(r => r.Ordinal)
            .ToList();
    }
}
