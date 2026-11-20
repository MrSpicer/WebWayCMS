namespace WebWayCMS.Data.Services.Registrations;

/// <summary>
/// One registration the code declares: its <paramref name="Key"/> (matched case-insensitively), how to build its
/// row when it has none (<paramref name="Create"/>), and how to bring an existing row's code-owned columns in
/// line (<paramref name="SyncCodeOwned"/> sets them on the row and returns whether anything changed; it must
/// never touch an admin-edited column).
/// </summary>
public sealed record RegistrationSyncItem<T>(string Key, Func<T> Create, Func<T, bool> SyncCodeOwned);
