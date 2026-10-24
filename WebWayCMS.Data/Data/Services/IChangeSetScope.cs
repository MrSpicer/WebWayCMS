using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Ambient change-set scoping. An enclosing operation (e.g. a save or a composite restore) opens a
/// scope; every <see cref="IContentStore{T}"/> write performed inside stamps its rows with the
/// scope's <see cref="Current"/> id, grouping them for history.
/// </summary>
public interface IChangeSetScope
{
    /// <summary>The current ambient change-set id, or <see cref="Guid.Empty"/> when none is open.</summary>
    Guid Current { get; }

    /// <summary>The user who opened the current scope, or null.</summary>
    Guid? CurrentUserId { get; }

    /// <summary>Opens a new scope and returns a handle that closes it.</summary>
    IDisposable Begin(ChangeSetKind kind, Guid? rootNodeId, string? note);

    /// <summary>
    /// The change note every version written now is stamped with (see <see cref="UseChangeNote"/>), or
    /// null/empty for the store's default note.
    /// </summary>
    string? CurrentChangeNote { get; }

    /// <summary>
    /// Makes <paramref name="note"/> the change note of every version written until the returned handle
    /// is disposed (which restores the previous note). Nests; opens no change set.
    /// </summary>
    IDisposable UseChangeNote(string note);
}
