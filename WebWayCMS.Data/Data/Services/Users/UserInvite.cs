namespace WebWayCMS.Data.Services.Users;

public sealed record UserInvite(string Email, UserProfileInput Profile, IReadOnlyList<string> Roles);
