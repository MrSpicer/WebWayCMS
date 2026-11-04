namespace WebWayCMS.Data.Services.Users;

public sealed record UserListItem(
    string UserId,
    string Email,
    string? DisplayName,
    string? FirstName,
    string? LastName,
    IReadOnlyList<string> Roles,
    UserStatus Status,
    bool IsInvitePending,
    DateTime? LastLoginUtc);
