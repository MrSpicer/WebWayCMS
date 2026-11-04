namespace WebWayCMS.Data.Services.Users;

public sealed record UserUpdate(string Email, string? PhoneNumber, UserProfileInput Profile);
