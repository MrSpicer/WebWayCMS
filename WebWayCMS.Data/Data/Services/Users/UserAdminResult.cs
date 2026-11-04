namespace WebWayCMS.Data.Services.Users;

/// <summary>
/// The outcome of a user-management write. <see cref="UserId"/> is the affected user (the new one for an invite);
/// <see cref="PreviousEmail"/> is set when an update changed the email, so the caller can mail both addresses.
/// </summary>
public sealed record UserAdminResult(bool Success, string? ErrorMessage = null, string? UserId = null, string? PreviousEmail = null)
{
    public static UserAdminResult Ok(string? userId = null, string? previousEmail = null) => new(true, null, userId, previousEmail);

    public static UserAdminResult Fail(string message) => new(false, message);
}
