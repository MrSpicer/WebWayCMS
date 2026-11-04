namespace WebWayCMS.Users;

/// <summary>
/// User-management settings, bound from the "UserManagement" configuration section.
/// </summary>
public sealed class UserManagementOptions
{
    public const string SectionName = "UserManagement";

    public const int DefaultInvitationLifetimeDays = 7;

    /// <summary>The longest an invitation may live; also keeps <c>TimeSpan.FromDays</c> far from overflowing.</summary>
    public const int MaxInvitationLifetimeDays = 90;

    /// <summary>The startup validation message.</summary>
    public const string ValidationMessage =
        "'UserManagement:InvitationLifetimeDays' must be between 1 and 90.";

    /// <summary>How long an invitation link stays valid, 1–<see cref="MaxInvitationLifetimeDays"/> days (checked at startup).</summary>
    public int InvitationLifetimeDays { get; set; } = DefaultInvitationLifetimeDays;

    public bool IsValid() => InvitationLifetimeDays is >= 1 and <= MaxInvitationLifetimeDays;
}
