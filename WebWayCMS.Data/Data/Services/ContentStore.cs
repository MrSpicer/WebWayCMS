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
    internal const string StaleVersionMessage = "This item was changed by someone else since you opened it.";
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
                                      && e.Version.IsCurrentDraft, ct);

    public Task<List<T>> GetAllCurrentDraftsAsync(CancellationToken ct = default)
        => _set.AsNoTracking()
               .Where(e => e.Version.Culture == string.Empty
                        && e.Version.Segment == string.Empty
                        && e.Version.IsCurrentDraft)
               .OrderByDescending(e => e.Version.CreatedUtc)
               .ToListAsync(ct);

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
            return new ContentWriteResult(true, VersionId: currentDraft.Id);

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
        return new ContentWriteResult(true, VersionId: currentDraft.Id);
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
            return new ContentWriteResult(true, VersionId: currentDraft.Id);
        }

        if (currentDraft.State == ContentVersionState.Published)
        {
            currentDraft.State = ContentVersionState.Draft;
            currentDraft.ChangeSetId = _changeSetScope.Current;
            await SaveChangesAsync(ct);
            return new ContentWriteResult(true, VersionId: currentDraft.Id);
        }

        return new ContentWriteResult(true, VersionId: currentDraft.Id);
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
            return new ContentWriteResult(true, VersionId: versionId);

        var maxNumber = await _set
            .Where(e => e.Version.NodeId == nodeId
                     && e.Version.Culture == string.Empty
                     && e.Version.Segment == string.Empty)
            .MaxAsync(e => (int?)e.Version.VersionNumber, ct) ?? 0;
        var newNumber = maxNumber + 1;

        var newVersion = BuildVersion(
            historical.Version, nodeId, newNumber,
            ContentVersionState.Draft, isCurrentDraft: true, now);

        // Clone the historical type-table row at the DTO level by detaching it and re-inserting
        // with a new key — no round-trip through a ViewModel, so every DTO field survives.
        _context.Entry(historical).State = EntityState.Detached;
        _context.Entry(historical.Version).State = EntityState.Detached;

        historical.VersionId = newVersion.Id;
        historical.Version = newVersion;

        if (currentDraft != null)
            currentDraft.Version.IsCurrentDraft = false;

        _set.Add(historical);
        try
        {
            await SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return new ContentWriteResult(false, StaleVersionMessage);
        }

        return new ContentWriteResult(true, VersionId: newVersion.Id);
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

        _set.RemoveRange(typeRows);
        _versions.RemoveRange(versionRows);
        _nodes.Remove(node);

        await SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        var entity = await _set.FirstOrDefaultAsync(e => e.VersionId == versionId, ct);
        if (entity == null)
            return false;

        _set.Remove(entity);
        _versions.Remove(entity.Version);
        await SaveChangesAsync(ct);
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

        var version = BuildVersion(entity.Version, node.Id, 0, ContentVersionState.Draft, isCurrentDraft: true, now);
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

        if (currentDraft.Version.State == ContentVersionState.Published)
        {
            // Publish a separate draft version: mint a new row and demote the published one.
            var maxNumber = await _set
                .Where(e => e.Version.NodeId == nodeId
                         && e.Version.Culture == string.Empty
                         && e.Version.Segment == string.Empty)
                .MaxAsync(e => (int?)e.Version.VersionNumber, ct) ?? 0;
            var newNumber = maxNumber + 1;

            var newVersion = BuildVersion(entity.Version, nodeId, newNumber, ContentVersionState.Draft, isCurrentDraft: true, now);

            currentDraft.Version.IsCurrentDraft = false;
            ApplyNodeFields(currentDraft.Version.Node, entity.Version.Node);

            entity.Version = newVersion;
            entity.VersionId = newVersion.Id;
            _set.Add(entity);

            try
            {
                await SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                return new ContentWriteResult(false, StaleVersionMessage);
            }

            return new ContentWriteResult(true, VersionId: newVersion.Id, NodeId: nodeId);
        }

        // Already a draft: update it in place so repeated saves do not mint a version each.
        currentDraft.Version.Title = entity.Version.Title ?? string.Empty;
        currentDraft.Version.Slug = NormalizeSlug(entity.Version.Title, entity.Version.Slug);
        currentDraft.Version.PublishStartUtc = entity.Version.PublishStartUtc;
        currentDraft.Version.PublishEndUtc = entity.Version.PublishEndUtc;
        currentDraft.Version.ChangeNote = entity.Version.ChangeNote;
        currentDraft.Version.ChangeSetId = _changeSetScope.Current;
        currentDraft.Version.CustomFields = entity.Version.CustomFields.Select(c => c with { }).ToList();

        ApplyNodeFields(currentDraft.Version.Node, entity.Version.Node);

        // The incoming entity's VersionId is the shared PK/FK; it may be unset (e.g. mapped from a
        // view model). SetValues must never rewrite it, so pin it to the existing draft's key first.
        entity.VersionId = currentDraft.VersionId;
        _context.Entry(currentDraft).CurrentValues.SetValues(entity);

        await SaveChangesAsync(ct);
        return new ContentWriteResult(true, VersionId: currentDraft.VersionId, NodeId: nodeId);
    }

    private ContentVersion BuildVersion(
        ContentVersion source,
        Guid nodeId,
        int versionNumber,
        ContentVersionState state,
        bool isCurrentDraft,
        DateTime now)
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
            ChangeNote = source.ChangeNote,
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

    private static void ApplyNodeFields(ContentNode target, ContentNode? source)
    {
        if (source == null) return;
        target.IsHidden = source.IsHidden;
        target.IsArchived = source.IsArchived;
        target.IsDeleted = source.IsDeleted;
    }

    private static string NormalizeSlug(string? title, string slug)
    {
        if (!string.IsNullOrWhiteSpace(slug))
            return Uri.EscapeDataString(slug);
        if (!string.IsNullOrWhiteSpace(title))
            return Uri.EscapeDataString(title);
        return string.Empty;
    }

    protected virtual Task<int> SaveChangesAsync(CancellationToken ct)
        => _context.SaveChangesAsync(ct);
}
