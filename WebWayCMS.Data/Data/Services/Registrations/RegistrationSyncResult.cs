namespace WebWayCMS.Data.Services.Registrations;

/// <summary>
/// What one <see cref="RegistrationSynchronizer.SyncAsync{T}"/> did: the keys it inserted, the keys whose
/// code-owned columns it updated, the inserts that failed, and the keys of rows the code no longer declares
/// (orphans, left in place).
/// </summary>
public sealed record RegistrationSyncResult(
    IReadOnlyList<string> Inserted,
    IReadOnlyList<string> Updated,
    IReadOnlyList<RegistrationSyncFailure> Failed,
    IReadOnlyList<string> Orphaned);
