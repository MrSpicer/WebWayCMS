namespace WebWayCMS.Identity;

/// <summary>
/// Public self-registration settings, bound from the "Identity" configuration section.
/// </summary>
public sealed class IdentityRegistrationOptions
{
    public const string SectionName = "Identity";

    /// <summary>
    /// When false, the Register page answers 404, the Login page hides its Register link, and an external
    /// sign-in can't create a new account (existing linked users still sign in). Defaults to true.
    /// </summary>
    public bool AllowRegistration { get; set; } = true;
}
