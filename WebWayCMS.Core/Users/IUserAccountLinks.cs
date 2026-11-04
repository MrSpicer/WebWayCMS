namespace WebWayCMS.Users;

/// <summary>Absolute links to the Identity account pages that account emails point at.</summary>
public interface IUserAccountLinks
{
    string ConfirmEmail(string userId, string code);

    string ResetPassword(string code);

    string AcceptInvitation(string userId, string code);
}
