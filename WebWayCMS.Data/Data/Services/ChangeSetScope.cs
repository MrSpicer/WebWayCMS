using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Scoped change-set tracking. <see cref="Begin"/> creates (and tracks, pending save) a
/// <see cref="ChangeSet"/> row and makes it ambient for the current async flow.
/// </summary>
public sealed class ChangeSetScope : IChangeSetScope
{
    private readonly CmsDbContext _context;
    private readonly IContentUserContext _userContext;
    private readonly AsyncLocal<ChangeSet?> _ambient = new();

    public ChangeSetScope(CmsDbContext context, IContentUserContext userContext)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    }

    public Guid Current => _ambient.Value?.Id ?? Guid.Empty;

    public Guid? CurrentUserId => _ambient.Value?.CreatedBy;

    public IDisposable Begin(ChangeSetKind kind, Guid? rootNodeId, string? note)
    {
        var changeSet = new ChangeSet
        {
            Id = Guid.NewGuid(),
            CreatedUtc = DateTime.UtcNow,
            CreatedBy = _userContext.CurrentUserId,
            Kind = kind,
            RootNodeId = rootNodeId,
            Note = note
        };
        _context.Set<ChangeSet>().Add(changeSet);

        var previous = _ambient.Value;
        _ambient.Value = changeSet;
        return new ScopeLease(this, previous, changeSet);
    }

    private void Close(ChangeSet? previous, ChangeSet changeSet)
    {
        if (_ambient.Value?.Id == changeSet.Id)
        {
            // If the enclosing operation never saved (e.g. validation failed before any store
            // write), stop tracking the change set so it does not leak into a later SaveChanges.
            var entry = _context.Entry(changeSet);
            if (entry.State == EntityState.Added)
                entry.State = EntityState.Detached;
        }

        _ambient.Value = previous;
    }

    private sealed class ScopeLease : IDisposable
    {
        private readonly ChangeSetScope _owner;
        private readonly ChangeSet? _previous;
        private readonly ChangeSet _changeSet;

        public ScopeLease(ChangeSetScope owner, ChangeSet? previous, ChangeSet changeSet)
        {
            _owner = owner;
            _previous = previous;
            _changeSet = changeSet;
        }

        public void Dispose() => _owner.Close(_previous, _changeSet);
    }
}
