using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services.Registrations;

/// <summary>
/// A versioned registration table (widgets, page types, content types). Code-owned columns are written to every
/// version row in place rather than through the store: saving a draft would mint a version per boot, and
/// publishing it would also publish an admin's in-progress edit. A new registration is saved and published.
/// </summary>
public sealed class VersionedRegistrationSyncTarget<T> : IRegistrationSyncTarget<T> where T : class, IVersionedContent
{
    private readonly CmsDbContext _context;
    private readonly IContentStore<T> _store;
    private readonly Func<T, string> _keyOf;

    public VersionedRegistrationSyncTarget(CmsDbContext context, IContentStore<T> store, Func<T, string> keyOf)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _keyOf = keyOf ?? throw new ArgumentNullException(nameof(keyOf));
    }

    public string KeyOf(T row) => _keyOf(row);

    public async Task<IReadOnlyList<T>> LoadRowsAsync(CancellationToken ct = default)
        => await _context.Set<T>().Where(e => !e.Version.Node.IsDeleted).ToListAsync(ct);

    public async Task SaveCodeOwnedChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();
    }

    public async Task<(bool Success, string? ErrorMessage)> InsertAsync(T row, CancellationToken ct = default)
    {
        var saved = await _store.SaveDraftAsync(row, null, ct);
        if (!saved.Success)
            return (false, saved.ErrorMessage);

        var published = await _store.PublishAsync(saved.NodeId, ct);
        return (published.Success, published.ErrorMessage);
    }
}
