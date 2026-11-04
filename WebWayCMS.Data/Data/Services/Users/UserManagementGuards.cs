namespace WebWayCMS.Data.Services.Users;

/// <summary>
/// The safety rules every user-management write runs before it changes anything. Pure, so the rules are
/// unit-tested on their own; <see cref="UserAdminService"/> supplies the facts.
/// </summary>
public static class UserManagementGuards
{
    public const string SelfMessage = "You can't {0} your own account.";

    public const string SelfEmailMessage =
        "You can't change your own email here. Use Account → Email, which keeps your current email until the new one is confirmed.";

    public const string LastAdminMessage = "This would leave no active Admin. Make another user an Admin first.";

    /// <summary>A refusal when the acting Admin targets their own account with a self-destructive action.</summary>
    public static string? SelfAction(string actorId, string targetId, string action) =>
        string.Equals(actorId, targetId, StringComparison.Ordinal) ? string.Format(SelfMessage, action) : null;

    /// <summary>
    /// A refusal when the acting Admin changes their own email through user management: the change unconfirms
    /// the account, which then can't sign in.
    /// </summary>
    public static string? SelfEmailChange(string actorId, string targetId) =>
        string.Equals(actorId, targetId, StringComparison.Ordinal) ? SelfEmailMessage : null;

    /// <summary>
    /// A refusal when the action takes an active Admin (in the Admin role, email confirmed, not locked, not deleted) out of
    /// service and no other active Admin would remain.
    /// </summary>
    public static string? LastActiveAdmin(bool targetIsActiveAdmin, int activeAdminCount) =>
        targetIsActiveAdmin && activeAdminCount <= 1 ? LastAdminMessage : null;
}
