namespace WebWayCMS.Data.Models;

/// <summary>Shared limits for <see cref="ApiTokenDTO"/>.</summary>
public static class ApiTokenRules
{
    /// <summary>Every issued token starts with this, so a leaked one is recognizable in logs and scanners.</summary>
    public const string TokenPrefix = "wwc_";

    public const int MaxNameLength = 100;

    /// <summary>Shortest plaintext accepted from configuration (dev seed tokens).</summary>
    public const int MinSeedTokenLength = 32;

    /// <summary>Characters of the plaintext kept as <see cref="ApiTokenDTO.Prefix"/>.</summary>
    public const int DisplayPrefixLength = 12;
}
