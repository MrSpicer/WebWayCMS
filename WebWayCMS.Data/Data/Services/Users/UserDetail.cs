namespace WebWayCMS.Data.Services.Users;

public sealed record UserDetail(
    string UserId,
    string Email,
    bool EmailConfirmed,
    string? PhoneNumber,
    string? DisplayName,
    string? FirstName,
    string? LastName,
    IReadOnlyList<string> Roles,
    UserStatus Status,
    bool IsInvitePending,
    bool HasPassword,
    bool TwoFactorEnabled,
    int PasskeyCount,
    IReadOnlyList<UserExternalLogin> ExternalLogins,
    int ActiveApiTokenCount,
    DateTimeOffset? LockoutEnd,
    int AccessFailedCount,
    DateTime? LastLoginUtc,
    DateTime? InvitedUtc,
    DateTime? DeletedUtc);
