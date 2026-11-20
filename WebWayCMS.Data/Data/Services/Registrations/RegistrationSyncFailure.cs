namespace WebWayCMS.Data.Services.Registrations;

/// <summary>A registration <see cref="RegistrationSynchronizer"/> could not insert, and why.</summary>
public sealed record RegistrationSyncFailure(string Key, string ErrorMessage);
