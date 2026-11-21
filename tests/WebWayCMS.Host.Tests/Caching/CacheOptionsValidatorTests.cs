using Microsoft.Extensions.Options;

using NUnit.Framework;

using WebWayCMS.Caching;

namespace WebWayCMS.Host.Tests.Caching;

[TestFixture]
public class CacheOptionsValidatorTests
{
    private readonly CacheOptionsValidator _validator = new();

    public static IEnumerable<TimeSpan> ExpirationsBelowMinimum()
    {
        yield return TimeSpan.MinValue;
        yield return TimeSpan.Zero;
        yield return TimeSpan.FromSeconds(1).Add(TimeSpan.FromMilliseconds(-1));
    }

    public static IEnumerable<TimeSpan> ExpirationBoundaries()
    {
        yield return TimeSpan.FromSeconds(1);
        yield return TimeSpan.FromDays(1);
    }

    public static IEnumerable<TimeSpan> PollIntervalsBelowMinimumNonZero()
    {
        yield return TimeSpan.FromSeconds(1).Add(TimeSpan.FromMilliseconds(-1));
        yield return TimeSpan.MinValue;
    }

    public static IEnumerable<TimeSpan> PollIntervalBoundaries()
    {
        yield return TimeSpan.Zero;
        yield return TimeSpan.FromSeconds(1);
        yield return TimeSpan.FromMinutes(10);
    }

    [Test]
    public void Validate_DefaultOptions_Succeeds()
    {
        var result = _validator.Validate(null, new CacheOptions());

        Assert.That(result.Failed, Is.False);
    }

    [TestCaseSource(nameof(ExpirationsBelowMinimum))]
    public void Validate_OutputCacheExpirationBelowOneSecond_Fails(TimeSpan value)
    {
        var result = _validator.Validate(null, new CacheOptions { OutputCacheExpiration = value });

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("OutputCacheExpiration"));
    }

    [Test]
    public void Validate_OutputCacheExpirationAboveOneDay_Fails()
    {
        var result = _validator.Validate(null, new CacheOptions { OutputCacheExpiration = TimeSpan.FromDays(2) });

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("OutputCacheExpiration"));
    }

    [TestCaseSource(nameof(ExpirationBoundaries))]
    public void Validate_OutputCacheExpirationAtBoundaries_Succeeds(TimeSpan value)
    {
        var result = _validator.Validate(null, new CacheOptions { OutputCacheExpiration = value });

        Assert.That(result.Failed, Is.False);
    }

    [TestCaseSource(nameof(ExpirationsBelowMinimum))]
    public void Validate_DataCacheExpirationBelowOneSecond_Fails(TimeSpan value)
    {
        var result = _validator.Validate(null, new CacheOptions { DataCacheExpiration = value });

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("DataCacheExpiration"));
    }

    [Test]
    public void Validate_DataCacheExpirationAboveOneDay_Fails()
    {
        var result = _validator.Validate(null, new CacheOptions { DataCacheExpiration = TimeSpan.FromDays(2) });

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("DataCacheExpiration"));
    }

    [TestCaseSource(nameof(ExpirationBoundaries))]
    public void Validate_DataCacheExpirationAtBoundaries_Succeeds(TimeSpan value)
    {
        var result = _validator.Validate(null, new CacheOptions { DataCacheExpiration = value });

        Assert.That(result.Failed, Is.False);
    }

    [Test]
    public void Validate_PollIntervalZero_Succeeds()
    {
        var result = _validator.Validate(null, new CacheOptions { GenerationPollInterval = TimeSpan.Zero });

        Assert.That(result.Failed, Is.False);
    }

    [TestCaseSource(nameof(PollIntervalsBelowMinimumNonZero))]
    public void Validate_PollIntervalBelowOneSecondNonZero_Fails(TimeSpan value)
    {
        var result = _validator.Validate(null, new CacheOptions { GenerationPollInterval = value });

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("GenerationPollInterval"));
    }

    [Test]
    public void Validate_PollIntervalAboveTenMinutes_Fails()
    {
        var result = _validator.Validate(null, new CacheOptions { GenerationPollInterval = TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(1)) });

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("GenerationPollInterval"));
    }

    [TestCaseSource(nameof(PollIntervalBoundaries))]
    public void Validate_PollIntervalAtBoundaries_Succeeds(TimeSpan value)
    {
        var result = _validator.Validate(null, new CacheOptions { GenerationPollInterval = value });

        Assert.That(result.Failed, Is.False);
    }
}
