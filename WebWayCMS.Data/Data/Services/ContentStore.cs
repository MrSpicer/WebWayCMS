using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// The single write/read engine for a versioned content type. Enforces the two versioning
/// invariants in code (the unique indexes are the DB backstop): exactly one
/// <see cref="ContentVersion.IsCurrentDraft"/> per variant, and at most one
/// <see cref="ContentVersionState.Published"/> per variant.
/// </summary>
public class ContentStore<T> : IContentStore<T> where T : class, IVersionedContent
{
    public const string StaleVersionMessage = "This item was changed by someone else since you opened it.";
    internal const string NotFoundMessage = "Content item not found.";

    private readonly CmsDbContext _context;
    private readonly DbSet<T> _set;
    private readonly DbSet<ContentVersion> _versions;
    private readonly DbSet<ContentNode> _nodes;
    private readonly IContentReadContext _readContext;
    private readonly IChangeSetScope _changeSetScope;
    private readonly IContentUserContext _userContext;
    private readonly string _contentTypeKey;

    public ContentStore(
        CmsDbContext context,
        IContentReadContext readContext,
        IChangeSetScope changeSetScope,
        IContentUserContext userContext,
        string contentTypeKey)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        _changeSetScope = changeSetScope ?? throw new ArgumentNullException(nameof(changeSetScope));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _contentTypeKey = contentTypeKey ?? throw new ArgumentNullException(nameof(contentTypeKey));

        _set = _context.Set<T>();
        _versions = _context.Set<ContentVersion>();
        _nodes = _context.Set<ContentNode>();
    }

    // ─── read-context aware ───────────────────────────────────────────────────

    public Task<T?> GetAsync(Guid nodeId, CancellationToken ct = default)
        => _set.AsNoTracking()
               .AtReadContext(_readContext)
               .FirstOrDefaultAsync(e => e.Version.NodeId == nodeId, ct);

    public Task<List<T>> GetAllAsync(CancellationToken ct = default)
        => _set.AsNoTracking()
               .AtReadContext(_readContext)
               .OrderByDescending(e => e.Version.CreatedUtc)
               .ToListAsync(ct);

    public Task<T?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return Task.FromResult<T?>(null);
        return _set.AsNoTracking()
                   .AtReadContext(_readContext)
                   .FirstOrDefaultAsync(e => e.Version.Slug == slug, ct);
    }

    public Task<List<T>> GetChildrenAsync(Guid parentNodeId, CancellationToken ct = default)
        => _set.AsNoTracking()
               .AtReadContext(_readContext)
               .Where(e => e.Version.Node.ParentNodeId == parentNodeId)
               .OrderByDescending(e => e.Version.CreatedUtc)
               .ToListAsync(ct);

    public Task<List<T>> GetRootsAsync(CancellationToken ct = default)
        => _set.AsNoTracking()
               .AtReadContext(_readContext)
               .Where(e => e.Version.Node.ParentNodeId == null)
               .OrderByDescending(e => e.Version.CreatedUtc)
               .ToListAsync(ct);

    public Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => _set.AsNoTracking()
               .AtReadContext(_readContext)
               .Where(predicate)
               .OrderByDescending(e => e.Version.CreatedUtc)
               .ToListAsync(ct);

    // ─── version-explicit ─────────────────────────────────────────────────────

    public Task<T?> GetVersionAsync(Guid versionId, CancellationToken ct = default)
        => _set.AsNoTracking().FirstOrDefaultAsync(e => e.VersionId == versionId, ct);

    public Task<List<T>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct = default)
        => _set.AsNoTracking()
               .Where(e => e.Version.NodeId == nodeId)
               .OrderByDescending(e => e.Version.VersionNumber)
               .ToListAsync(ct);

    // ─── current-draft reads (admin) ──────────────────────────────────────────

    public Task<T?> GetCurrentDraftAsync(Guid nodeId, CancellationToken ct = default)
        => _set.AsNoTracking()
               .FirstOrDefaultAsync(e => e.Version.NodeId == nodeId
                                      && e.Version.Culture == string.Empty
                                      && e.Version.Segment == string.Empty
                                      && e.Version.IsCurrentDraft
                                      && !e.Version.Node.IsDeleted, ct);

    public Task<T?> GetCurrentDraftBySlugAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return Task.FromResult<T?>(null);
        return _set.AsNoTracking()
                   .FirstOrDefaultAsync(e => e.Version.Culture == string.Empty
                                          && e.Version.Segment == string.Empty
                                          && e.Version.IsCurrentDraft
                                          && !e.Version.Node.IsDeleted
                                          && e.Version.Slug == slug, ct);
    }

    public Task<List<T>> GetAllCurrentDraftsAsync(CancellationToken ct = default)
        => CurrentDrafts()
               .OrderByDescending(e => e.Version.CreatedUtc)
               .ToListAsync(ct);

    public Task<List<T>> FindCurrentDraftsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => CurrentDrafts()
               .Where(predicate)
               .OrderByDescending(e => e.Version.CreatedUtc)
               .ToListAsync(ct);

    public Task<Dictionary<TKey, int>> CountCurrentDraftsByAsync<TKey>(
        Expression<Func<T, TKey>> keySelector, CancellationToken ct = default)
        where TKey : notnull
        => CurrentDrafts()
               .GroupBy(keySelector)
               .Select(g => new { g.Key, Count = g.Count() })
               .ToDictionaryAsync(g => g.Key, g => g.Count, ct);

    /// <summary>The invariant-culture, live (not deleted) current draft of every node.</summary>
    private IQueryable<T> CurrentDrafts()
        => _set.AsNoTracking()
               .Where(e => e.Version.Culture == string.Empty
                        && e.Version.Segment == string.Empty
                        && e.Version.IsCurrentDraft
                        && !e.Version.Node.IsDeleted);

    public Task<List<T>> GetCurrentDraftChildrenAsync(Guid? parentNodeId, CancellationToken ct = default)
        => _set.AsNoTracking()
               .Where(e => e.Version.Node.ParentNodeId == parentNodeId
                        && e.Version.Culture == string.Empty
                        && e.Version.Segment == string.Empty
                        && e.Version.IsCurrentDraft
                        && !e.Version.Node.IsDeleted)
               .OrderByDescending(e => e.Version.CreatedUtc)
               .ToListAsync(ct);

    public Task<HashSet<Guid>> GetPublishedNodeIdsAsync(CancellationToken ct = default)
        => _set.AsNoTracking()
               .Where(e => e.Version.Culture == string.Empty
                        && e.Version.Segment == string.Empty
                        && e.Version.State == ContentVersionState.Published
                        && !e.Version.Node.IsDeleted)
               .Select(e => e.Version.NodeId)
               .Distinct()
               .ToHashSetAsync(ct);

    // ─── writes ───────────────────────────────────────────────────────────────

    public async Task<ContentWriteResult> SaveDraftAsync(T entity, int? expectedVersionNumber, CancellationToken ct = default)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));

        var nodeId = entity.Version.Node?.Id ?? Guid.Empty;
        using var _ = EnsureScope(ChangeSetKind.Save, nodeId == Guid.Empty ? null : nodeId);

        if (nodeId == Guid.Empty)
            return await CreateDraftAsync(entity, ct);

        return await EditDraftAsync(entity, nodeId, expectedVersionNumber, ct);
    }

    public async Task<ContentWriteResult> PublishAsync(Guid nodeId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        using var _ = EnsureScope(ChangeSetKind.Publish, nodeId);

        var currentDraft = await FindCurrentDraftAsync(nodeId, ct);
        if (currentDraft == null)
            return new ContentWriteResult(false, NotFoundMessage);

        if (currentDraft.State == ContentVersionState.Published)
            return new ContentWriteResult(true, VersionId: currentDraft.Id, NodeId: nodeId);

        var otherPublished = await _versions
            .Where(v => v.NodeId == nodeId
                     && v.Culture == string.Empty
                     && v.Segment == string.Empty
                     && v.State == ContentVersionState.Published
                     && v.Id != currentDraft.Id)
            .ToListAsync(ct);

        // Archive prior published versions in their own SaveChanges so the promotion below never
        // transiently coexists with another Published row — the filtered unique index
        // UX_ContentVersion_PublishedVariant is checked per-statement, and EF Core does not
        // guarantee the order of UPDATEs within a single SaveChanges.
        if (otherPublished.Count > 0)
        {
            foreach (var v in otherPublished)
                v.State = ContentVersionState.Archived;
            await SaveChangesAsync(ct);
        }

        currentDraft.State = ContentVersionState.Published;
        currentDraft.PublishedBy = _userContext.CurrentUserId;
        currentDraft.PublishedUtc = now;
        currentDraft.ChangeSetId = _changeSetScope.Current;

        await SaveChangesAsync(ct);

        // Report the version this publish archived (at most one Published per variant), so a caller
        // whose follow-up step fails can hand it to RevertPublishAsync.
        return new ContentWriteResult(true, VersionId: currentDraft.Id, NodeId: nodeId,
            DisplacedVersionId: otherPublished.Select(v => v.Id).FirstOrDefault());
    }

    public async Task<ContentWriteResult> UnpublishAsync(Guid nodeId, CancellationToken ct = default)
    {
        using var _ = EnsureScope(ChangeSetKind.Unpublish, nodeId);

        var currentDraft = await FindCurrentDraftAsync(nodeId, ct);
        if (currentDraft == null)
            return new ContentWriteResult(false, NotFoundMessage);

        if (currentDraft.State == ContentVersionState.Draft)
        {
            var published = await _versions
                .Where(v => v.NodeId == nodeId
                         && v.Culture == string.Empty
                         && v.Segment == string.Empty
                         && v.State == ContentVersionState.Published)
                .ToListAsync(ct);
            foreach (var v in published)
                v.State = ContentVersionState.Archived;
            await SaveChangesAsync(ct);
            return new ContentWriteResult(true, VersionId: currentDraft.Id, NodeId: nodeId);
        }

        if (currentDraft.State == ContentVersionState.Published)
        {
            currentDraft.State = ContentVersionState.Draft;
            currentDraft.ChangeSetId = _changeSetScope.Current;
            await SaveChangesAsync(ct);
            return new ContentWriteResult(true, VersionId: currentDraft.Id, NodeId: nodeId);
        }

        return new ContentWriteResult(true, VersionId: currentDraft.Id, NodeId: nodeId);
    }

    /// <summary>
    /// Rolls a failed publish attempt back to the pre-publish state, undoing only what that publish
    /// changed: <paramref name="publishedVersionId"/> (the publish result's <c>VersionId</c>) returns to
    /// Draft, and <paramref name="displacedVersionId"/> (its <c>DisplacedVersionId</c>; empty when the
    /// publish archived nothing) is restored to Published. Unlike <see cref="UnpublishAsync"/>, this
    /// never takes down a version that was live before the attempt, and never brings back one that
    /// was not. A version that is no longer in the state the publish left it in is left alone.
    /// </summary>
    public async Task<ContentWriteResult> RevertPublishAsync(Guid nodeId, Guid publishedVersionId, Guid displacedVersionId, CancellationToken ct = default)
    {
        using var _ = EnsureScope(ChangeSetKind.Unpublish, nodeId);

        var published = await _versions
            .FirstOrDefaultAsync(v => v.Id == publishedVersionId && v.NodeId == nodeId, ct);
        if (published == null)
            return new ContentWriteResult(false, NotFoundMessage);

        if (published.State != ContentVersionState.Published)
            return new ContentWriteResult(true, VersionId: published.Id);

        var displaced = displacedVersionId == Guid.Empty
            ? null
            : await _versions.FirstOrDefaultAsync(v => v.Id == displacedVersionId
                                                    && v.NodeId == nodeId
                                                    && v.State == ContentVersionState.Archived, ct);

        // Demote in its own SaveChanges before promoting, so the filtered unique index
        // UX_ContentVersion_PublishedVariant never sees two Published rows (EF Core does not order
        // UPDATEs within one SaveChanges). One transaction keeps a failed promotion from leaving
        // the node with nothing live.
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            published.State = ContentVersionState.Draft;
            published.ChangeSetId = _changeSetScope.Current;
            await SaveChangesAsync(ct);

            if (displaced != null)
            {
                displaced.State = ContentVersionState.Published;
                await SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            // The rollback restores the rows but not the change tracker; the context is scoped and
            // shared, so the abandoned state flips are detached before the next SaveChanges replays them.
            _context.Entry(published).State = EntityState.Detached;
            if (displaced != null)
                _context.Entry(displaced).State = EntityState.Detached;
            return new ContentWriteResult(false, StaleVersionMessage);
        }

        return new ContentWriteResult(true, VersionId: displaced?.Id ?? published.Id);
    }

    public async Task<ContentWriteResult> RestoreAsync(Guid versionId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var historical = await _set
            .Include(e => e.Version)
            .FirstOrDefaultAsync(e => e.VersionId == versionId, ct);
        if (historical == null)
            return new ContentWriteResult(false, "Version not found.");

        var nodeId = historical.Version.NodeId;
        using var _ = EnsureScope(ChangeSetKind.Restore, nodeId);

        var currentDraft = await _set
            .Include(e => e.Version)
            .FirstOrDefaultAsync(e => e.Version.NodeId == nodeId
                                   && e.Version.Culture == string.Empty
                                   && e.Version.Segment == string.Empty
                                   && e.Version.IsCurrentDraft, ct);

        if (currentDraft != null && currentDraft.VersionId == versionId)
            return new ContentWriteResult(true, VersionId: versionId, NodeId: nodeId);

        var maxNumber = await _set
            .Where(e => e.Version.NodeId == nodeId
                     && e.Version.Culture == string.Empty
                     && e.Version.Segment == string.Empty)
            .MaxAsync(e => (int?)e.Version.VersionNumber, ct) ?? 0;
        var newNumber = maxNumber + 1;

        var newVersion = BuildVersion(
            historical.Version, nodeId, newNumber,
            ContentVersionState.Draft, isCurrentDraft: true, now,
            defaultNote: $"Restored from version {historical.Version.VersionNumber}");

        // Clone the historical type-table row at the DTO level by detaching it and re-inserting
        // with a new key — no round-trip through a ViewModel, so every DTO field survives.
        _context.Entry(historical).State = EntityState.Detached;
        _context.Entry(historical.Version).State = EntityState.Detached;

        historical.VersionId = newVersion.Id;
        historical.Version = newVersion;

        // Demote, then insert, in two saves inside one transaction (joining the caller's when one is open):
        // see EditDraftAsync for why one combined save trips UX_ContentVersion_DraftVariant.
        var oldVersion = currentDraft?.Version;
        var ambient = _context.Database.CurrentTransaction != null;
        await using var transaction = ambient ? null : await _context.Database.BeginTransactionAsync(ct);
        try
        {
            if (oldVersion != null)
            {
                oldVersion.IsCurrentDraft = false;
                await SaveChangesAsync(ct);
            }

            _set.Add(historical);
            await SaveChangesAsync(ct);

            if (!ambient)
                await transaction!.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            // The scoped context is shared, so the abandoned demotion and insert are detached before the
            // next SaveChanges would replay them (the rollback restores only the rows).
            if (!ambient)
                await transaction!.RollbackAsync(CancellationToken.None);
            _context.Entry(historical).State = EntityState.Detached;
            _context.Entry(newVersion).State = EntityState.Detached;
            if (currentDraft != null)
                _context.Entry(currentDraft).State = EntityState.Detached;
            if (oldVersion != null)
                _context.Entry(oldVersion).State = EntityState.Detached;
            if (ex is DbUpdateException)
                return new ContentWriteResult(false, StaleVersionMessage);
            throw;
        }

        return new ContentWriteResult(true, VersionId: newVersion.Id, NodeId: nodeId);
    }

    public async Task<bool> DeleteAsync(Guid nodeId, bool softDelete, CancellationToken ct = default)
    {
        using var _ = EnsureScope(ChangeSetKind.Delete, nodeId);

        var node = await _nodes.FirstOrDefaultAsync(n => n.Id == nodeId, ct);
        if (node == null)
            return false;

        if (softDelete)
        {
            node.IsDeleted = true;
            await SaveChangesAsync(ct);
            return true;
        }

        var typeRows = await _set.Where(e => e.Version.NodeId == nodeId).ToListAsync(ct);
        var versionRows = await _versions.Where(v => v.NodeId == nodeId).ToListAsync(ct);

        // A node that is gone takes every page-set history entry that captured it with it: an entry
        // missing one of its versions could no longer be previewed or reverted faithfully.
        await RemoveSnapshotsReferencingAsync(versionRows.Select(v => v.Id).ToList(), ct);

        _set.RemoveRange(typeRows);
        _versions.RemoveRange(versionRows);
        _nodes.Remove(node);

        try
        {
            await SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A recorder may have inserted a snapshot referencing one of these versions between the
            // removal sweep and this save; refuse instead of letting the FK violation escape.
            _context.ChangeTracker.Clear();
            return false;
        }
        return true;
    }

    public async Task<bool> DeleteVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        var entity = await _set
            .Include(e => e.Version)
            .FirstOrDefaultAsync(e => e.VersionId == versionId, ct);
        if (entity == null)
            return false;

        // Match the admin UI, which hides Delete on the latest version: never delete the
        // highest-numbered version (that also covers a node's single version for free). Scope the max
        // to the same variant the deleted version belongs to — a non-empty culture/segment otherwise
        // collapses the aggregate to 0 and makes every version permanently undeletable.
        var culture = entity.Version.Culture;
        var segment = entity.Version.Segment;
        var maxVersion = await _set
            .Where(e => e.Version.NodeId == entity.Version.NodeId
                     && e.Version.Culture == culture
                     && e.Version.Segment == segment)
            .MaxAsync(e => (int?)e.Version.VersionNumber, ct) ?? 0;

        if (entity.Version.VersionNumber >= maxVersion)
            return false;

        // A user-initiated delete of a captured version is refused upstream (the handlers check
        // GetSnapshotsReferencingVersionAsync first); reaching here means an internal cleanup such as a
        // zone delete, which takes the referencing entries with it.
        await RemoveSnapshotsReferencingAsync([versionId], ct);

        _set.Remove(entity);
        _versions.Remove(entity.Version);
        try
        {
            await SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A recorder may have inserted a snapshot referencing this version between the removal sweep
            // and this save; refuse instead of letting the FK violation escape.
            _context.ChangeTracker.Clear();
            return false;
        }
        return true;
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private async Task<ContentWriteResult> CreateDraftAsync(T entity, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var node = new ContentNode
        {
            Id = Guid.NewGuid(),
            ContentTypeKey = _contentTypeKey,
            ParentNodeId = entity.Version.Node?.ParentNodeId,
            SiteId = entity.Version.Node?.SiteId,
            IsHidden = entity.Version.Node?.IsHidden ?? false,
            IsArchived = entity.Version.Node?.IsArchived ?? false,
            IsDeleted = entity.Version.Node?.IsDeleted ?? false,
            CreatedUtc = now,
            CreatedBy = _userContext.CurrentUserId
        };

        var version = BuildVersion(entity.Version, node.Id, 0, ContentVersionState.Draft, isCurrentDraft: true, now, defaultNote: "Created");
        version.Node = node;

        entity.Version = version;
        entity.VersionId = version.Id;

        _set.Add(entity);
        await SaveChangesAsync(ct);
        return new ContentWriteResult(true, VersionId: version.Id, NodeId: node.Id);
    }

    private async Task<ContentWriteResult> EditDraftAsync(T entity, Guid nodeId, int? expectedVersionNumber, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var currentDraft = await _set
            .Include(e => e.Version)
            .FirstOrDefaultAsync(e => e.Version.NodeId == nodeId
                                   && e.Version.Culture == string.Empty
                                   && e.Version.Segment == string.Empty
                                   && e.Version.IsCurrentDraft, ct);

        if (currentDraft == null)
            return new ContentWriteResult(false, NotFoundMessage);

        if (expectedVersionNumber.HasValue && currentDraft.Version.VersionNumber != expectedVersionNumber.Value)
            return new ContentWriteResult(false, StaleVersionMessage);

        // Every save is history: versions are never rewritten in place, so each save mints a new current
        // draft and the previous one (published or not) stays restorable.
        var maxNumber = await _set
            .Where(e => e.Version.NodeId == nodeId
                     && e.Version.Culture == string.Empty
                     && e.Version.Segment == string.Empty)
            .MaxAsync(e => (int?)e.Version.VersionNumber, ct) ?? 0;

        var newVersion = BuildVersion(entity.Version, nodeId, maxNumber + 1, ContentVersionState.Draft,
            isCurrentDraft: true, now, defaultNote: "Edited");

        // A caller may hand back an instance the context already tracks (identity resolution can even
        // make it the same instance as currentDraft). It is re-keyed as a new row, so it must be
        // detached first (EF never lets a tracked key change) — before any save, so edits the caller
        // made to the tracked instance are never flushed onto the old version's row. The ContentVersion
        // and ContentNode rows are tracked separately, so the demotion below persists regardless.
        if (_context.Entry(entity).State != EntityState.Detached)
            _context.Entry(entity).State = EntityState.Detached;

        // The demotion of the old draft and the promotion of the new one are separate SaveChanges calls
        // inside one transaction: the filtered unique index UX_ContentVersion_DraftVariant is checked
        // per-statement and EF Core does not order INSERTs before UPDATEs within one SaveChanges, so a
        // combined save would transiently hold two current drafts and fail spuriously. When a caller
        // already opened a transaction (no nested transactions on Npgsql), the two saves join it as-is.
        // Captured before the swap below: when entity is currentDraft, currentDraft.Version becomes newVersion.
        var oldVersion = currentDraft.Version;
        var ambient = _context.Database.CurrentTransaction != null;
        await using var transaction = ambient ? null : await _context.Database.BeginTransactionAsync(ct);
        try
        {
            oldVersion.IsCurrentDraft = false;
            ApplyNodeFields(oldVersion.Node, entity.Version.Node);
            await SaveChangesAsync(ct);

            entity.Version = newVersion;
            entity.VersionId = newVersion.Id;
            _set.Add(entity);
            await SaveChangesAsync(ct);

            if (!ambient)
                await transaction!.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The scoped context is shared, so the abandoned demotion and insert are detached before the
            // next SaveChanges would replay them (the transaction's rollback restores only the rows).
            if (!ambient)
                await transaction!.RollbackAsync(CancellationToken.None);
            _context.Entry(entity).State = EntityState.Detached;
            _context.Entry(newVersion).State = EntityState.Detached;
            _context.Entry(currentDraft).State = EntityState.Detached;
            _context.Entry(oldVersion).State = EntityState.Detached;
            return new ContentWriteResult(false, StaleVersionMessage);
        }
        catch
        {
            if (!ambient)
                await transaction!.RollbackAsync(CancellationToken.None);
            _context.Entry(entity).State = EntityState.Detached;
            _context.Entry(newVersion).State = EntityState.Detached;
            _context.Entry(currentDraft).State = EntityState.Detached;
            _context.Entry(oldVersion).State = EntityState.Detached;
            throw;
        }

        return new ContentWriteResult(true, VersionId: newVersion.Id, NodeId: nodeId);
    }

    /// <summary>
    /// A new version row carrying <paramref name="source"/>'s fields. Its change note is the ambient one
    /// (<see cref="IChangeSetScope.UseChangeNote"/>), else <paramref name="defaultNote"/> — never the
    /// source's, so a note describes only the save that wrote it.
    /// </summary>
    private ContentVersion BuildVersion(
        ContentVersion source,
        Guid nodeId,
        int versionNumber,
        ContentVersionState state,
        bool isCurrentDraft,
        DateTime now,
        string defaultNote)
        => new()
        {
            Id = Guid.NewGuid(),
            NodeId = nodeId,
            VersionNumber = versionNumber,
            Culture = string.Empty,
            Segment = string.Empty,
            State = state,
            IsCurrentDraft = isCurrentDraft,
            Title = source.Title ?? string.Empty,
            Slug = NormalizeSlug(source.Title, source.Slug),
            CreatedBy = _userContext.CurrentUserId,
            CreatedUtc = now,
            PublishStartUtc = source.PublishStartUtc,
            PublishEndUtc = source.PublishEndUtc,
            ChangeNote = string.IsNullOrEmpty(_changeSetScope.CurrentChangeNote) ? defaultNote : _changeSetScope.CurrentChangeNote,
            ChangeSetId = _changeSetScope.Current,
            CustomFields = source.CustomFields.Select(c => c with { }).ToList()
        };

    private Task<ContentVersion?> FindCurrentDraftAsync(Guid nodeId, CancellationToken ct)
        => _versions.FirstOrDefaultAsync(
            v => v.NodeId == nodeId
              && v.Culture == string.Empty
              && v.Segment == string.Empty
              && v.IsCurrentDraft, ct);

    private IDisposable? EnsureScope(ChangeSetKind kind, Guid? rootNodeId)
        => _changeSetScope.Current == Guid.Empty
            ? _changeSetScope.Begin(kind, rootNodeId, null)
            : null;

    /// <summary>
    /// Stages the removal of every page-set history entry that references one of
    /// <paramref name="versionIds"/> (as its page version or as an item), so the caller's
    /// SaveChanges deletes the entries before the versions their FKs point at.
    /// </summary>
    private async Task RemoveSnapshotsReferencingAsync(List<Guid> versionIds, CancellationToken ct)
    {
        var viaItems = await _context.Set<PageSetSnapshotItem>()
            .Where(i => versionIds.Contains(i.ItemVersionId))
            .Select(i => i.SnapshotId)
            .ToListAsync(ct);

        var snapshots = await _context.Set<PageSetSnapshot>()
            .Include(s => s.Items)
            .Where(s => versionIds.Contains(s.PageVersionId) || viaItems.Contains(s.Id))
            .ToListAsync(ct);

        _context.Set<PageSetSnapshot>().RemoveRange(snapshots);
    }

    private static void ApplyNodeFields(ContentNode target, ContentNode? source)
    {
        if (source == null) return;
        target.IsHidden = source.IsHidden;
        target.ParentNodeId = source.ParentNodeId;
    }

    private static string NormalizeSlug(string? title, string slug)
        => Slugs.SlugNormalizer.Normalize(string.IsNullOrWhiteSpace(slug) ? title : slug);

    protected virtual Task<int> SaveChangesAsync(CancellationToken ct)
        => _context.SaveChangesAsync(ct);
}
