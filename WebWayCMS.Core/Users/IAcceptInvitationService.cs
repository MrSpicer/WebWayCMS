namespace WebWayCMS.Users;

/// <summary>
/// The invitation-link flows behind the AcceptInvitation page: previewing a link (GET) and accepting it
/// (POST). Every bad link looks the same: <see cref="InvitationPreview"/> is null and acceptance reports
/// <see cref="AcceptInvitationResult.LinkInvalid"/>.
/// </summary>
public interface IAcceptInvitationService
{
    /// <summary>The invited email when the link is still good; null otherwise.</summary>
    Task<InvitationPreview?> PreviewAsync(string? userId, string? code, CancellationToken ct = default);

    /// <summary>Sets the invited user's password, which also confirms their email.</summary>
    Task<AcceptInvitationResult> AcceptAsync(string? userId, string? code, string password, CancellationToken ct = default);
}
