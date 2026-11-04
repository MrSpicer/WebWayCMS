namespace WebWayCMS.Data.Services.Users;

public sealed record UserExternalLogin(string LoginProvider, string ProviderKey, string? DisplayName);
