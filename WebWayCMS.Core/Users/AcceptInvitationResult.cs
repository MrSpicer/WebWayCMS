namespace WebWayCMS.Users;

/// <summary>
/// The outcome of accepting an invitation: the link was used (<see cref="LinkInvalid"/> says it had already
/// died), the password failed the policy (<see cref="Errors"/>), the email-confirm write failed
/// (<see cref="ErrorMessage"/>, <see cref="PasswordSaved"/> true — the account needs an administrator's
/// confirmation email), or the invitation was accepted (<see cref="Succeeded"/>).
/// </summary>
public sealed record AcceptInvitationResult
{
    public static AcceptInvitationResult InvalidLink() => new() { LinkInvalid = true };

    public static AcceptInvitationResult Rejected(string email, IReadOnlyList<string> errors) =>
        new() { Email = email, Errors = errors };

    public static AcceptInvitationResult ConfirmFailed(string email, string message) =>
        new() { Email = email, PasswordSaved = true, ErrorMessage = message };

    public static AcceptInvitationResult Accept(string email) => new() { Succeeded = true, Email = email };

    /// <summary>The invitation link is invalid, expired, or already used.</summary>
    public bool LinkInvalid { get; private init; }

    /// <summary>The invitation was accepted: the password is set and the email confirmed.</summary>
    public bool Succeeded { get; private init; }

    /// <summary>The invited address, shown on the page.</summary>
    public string? Email { get; private init; }

    /// <summary>The password write succeeded even though the overall result did not (a failed confirmation).</summary>
    public bool PasswordSaved { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyList<string> Errors { get; private init; } = [];
}
