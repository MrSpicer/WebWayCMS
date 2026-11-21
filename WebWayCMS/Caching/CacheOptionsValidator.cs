using Microsoft.Extensions.Options;

namespace WebWayCMS.Caching;

/// <summary>
/// Bounds for <see cref="CacheOptions"/>: expirations between 1 second and 1 day (a shorter
/// expiration than a poll interval makes the stamp pointless; a longer one outlives most edits),
/// and a poll interval of zero (off) or up to 10 minutes (longer makes split-host staleness
/// unacceptably visible).
/// </summary>
public sealed class CacheOptionsValidator : IValidateOptions<CacheOptions>
{
    public static readonly TimeSpan MinExpiration = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxExpiration = TimeSpan.FromDays(1);
    public static readonly TimeSpan MaxPollInterval = TimeSpan.FromMinutes(10);

    public ValidateOptionsResult Validate(string? name, CacheOptions options)
    {
        if (options.OutputCacheExpiration < MinExpiration || options.OutputCacheExpiration > MaxExpiration)
            return ValidateOptionsResult.Fail(
                $"{CacheOptions.SectionName}:OutputCacheExpiration must be between {MinExpiration} and {MaxExpiration}.");
        if (options.DataCacheExpiration < MinExpiration || options.DataCacheExpiration > MaxExpiration)
            return ValidateOptionsResult.Fail(
                $"{CacheOptions.SectionName}:DataCacheExpiration must be between {MinExpiration} and {MaxExpiration}.");
        if (options.GenerationPollInterval != TimeSpan.Zero
            && (options.GenerationPollInterval < MinExpiration || options.GenerationPollInterval > MaxPollInterval))
            return ValidateOptionsResult.Fail(
                $"{CacheOptions.SectionName}:GenerationPollInterval must be 0 (off) or between {MinExpiration} and {MaxPollInterval}.");
        return ValidateOptionsResult.Success;
    }
}
