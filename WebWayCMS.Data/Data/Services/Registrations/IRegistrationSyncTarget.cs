namespace WebWayCMS.Data.Services.Registrations;

/// <summary>Where <see cref="RegistrationSynchronizer"/> reads and writes one registration table.</summary>
public interface IRegistrationSyncTarget<T>
{
    /// <summary>The registration key of a row.</summary>
    string KeyOf(T row);

    /// <summary>
    /// Every row to sync, tracked, so code-owned changes made to them are saved by
    /// <see cref="SaveCodeOwnedChangesAsync"/>. A versioned table returns every version of every node that is not
    /// deleted (a code-owned column is a fact about the code, true of every version).
    /// </summary>
    Task<IReadOnlyList<T>> LoadRowsAsync(CancellationToken ct = default);

    /// <summary>Saves the code-owned changes made to the loaded rows, then stops tracking them.</summary>
    Task SaveCodeOwnedChangesAsync(CancellationToken ct = default);

    /// <summary>Adds a new registration (a versioned one is published, so it is live at once).</summary>
    Task<(bool Success, string? ErrorMessage)> InsertAsync(T row, CancellationToken ct = default);
}
