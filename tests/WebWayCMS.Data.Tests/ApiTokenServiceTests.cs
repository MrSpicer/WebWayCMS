using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class ApiTokenServiceTests
{
    private const string UserId = "user-1";
    private const string OtherUserId = "user-2";
    private const string SeedPlain = "dev-seed-token-0123456789abcdefghijklmnop";

    private string _db = null!;

    [SetUp]
    public void SetUp()
    {
        _db = TestContexts.NewDb();
        using var ctx = TestContexts.Cms(_db);
        ctx.Set<IdentityUser>().Add(new IdentityUser { Id = UserId, UserName = "a@example.com", Email = "a@example.com" });
        ctx.Set<IdentityUser>().Add(new IdentityUser { Id = OtherUserId, UserName = "b@example.com", Email = "b@example.com" });
        ctx.SaveChanges();
    }

    private ApiTokenService NewService() => new(TestContexts.Cms(_db));

    private async Task<ApiTokenDTO> StoredAsync(Guid id)
    {
        await using var ctx = TestContexts.Cms(_db);
        return await ctx.Set<ApiTokenDTO>().AsNoTracking().SingleAsync(t => t.Id == id);
    }

    private async Task UpdateAsync(Guid id, Action<ApiTokenDTO> change)
    {
        await using var ctx = TestContexts.Cms(_db);
        change(await ctx.Set<ApiTokenDTO>().SingleAsync(t => t.Id == id));
        await ctx.SaveChangesAsync();
    }

    [Test]
    public void Constructor_NullContext_Throws() =>
        Assert.That(() => new ApiTokenService(null!), Throws.ArgumentNullException);

    // --- CreateAsync ---

    [Test]
    public async Task CreateAsync_IssuesAPrefixedToken_AndStoresOnlyItsHash()
    {
        var expires = DateTime.UtcNow.AddDays(30);

        var (success, error, plain, token) = await NewService().CreateAsync(UserId, "  Laptop  ", expires);

        var stored = await StoredAsync(token!.Id);
        Assert.Multiple(() =>
        {
            Assert.That((success, error), Is.EqualTo((true, (string?)null)));
            Assert.That(plain, Does.StartWith(ApiTokenRules.TokenPrefix));
            Assert.That(plain!.Length, Is.GreaterThan(40));
            Assert.That(stored.TokenHash, Is.EqualTo(ApiTokenService.Hash(plain)));
            Assert.That(stored.TokenHash, Does.Not.Contain(plain));
            Assert.That(stored.Prefix, Is.EqualTo(plain[..ApiTokenRules.DisplayPrefixLength]));
            Assert.That(stored.Name, Is.EqualTo("Laptop"), "the name is trimmed");
            Assert.That(stored.UserId, Is.EqualTo(UserId));
            Assert.That(stored.ExpiresUtc, Is.EqualTo(expires));
            Assert.That(stored.LastUsedUtc, Is.Null);
            Assert.That(stored.RevokedUtc, Is.Null);
        });
    }

    [Test]
    public async Task CreateAsync_TwoTokens_AreDifferent()
    {
        var first = await NewService().CreateAsync(UserId, "One", null);
        var second = await NewService().CreateAsync(UserId, "Two", null);

        Assert.That(first.PlainToken, Is.Not.EqualTo(second.PlainToken));
    }

    [TestCase(null, "A user is required.")]
    [TestCase(" ", "A user is required.")]
    public async Task CreateAsync_NoUser_IsRefused(string? userId, string expected)
    {
        var result = await NewService().CreateAsync(userId!, "Name", null);

        Assert.That((result.Success, result.ErrorMessage, result.PlainToken), Is.EqualTo((false, expected, (string?)null)));
    }

    [Test]
    public async Task CreateAsync_InvalidName_IsRefused()
    {
        var empty = await NewService().CreateAsync(UserId, " ", null);
        var tooLong = await NewService().CreateAsync(UserId, new string('x', ApiTokenRules.MaxNameLength + 1), null);

        Assert.Multiple(() =>
        {
            Assert.That(empty.ErrorMessage, Is.EqualTo("A token name is required."));
            Assert.That(tooLong.ErrorMessage, Does.Contain("at most"));
        });
    }

    [Test]
    public async Task CreateAsync_PastExpiry_IsRefused()
    {
        var result = await NewService().CreateAsync(UserId, "Old", DateTime.UtcNow.AddMinutes(-1));

        Assert.That(result.ErrorMessage, Is.EqualTo("The expiry must be in the future."));
    }

    [Test]
    public async Task CreateAsync_NameOfALiveToken_IsRefused_ButARevokedOnesNameIsFree()
    {
        var first = await NewService().CreateAsync(UserId, "Laptop", null);
        var duplicate = await NewService().CreateAsync(UserId, "Laptop", null);
        var otherUser = await NewService().CreateAsync(OtherUserId, "Laptop", null);
        await NewService().RevokeAsync(first.Token!.Id, UserId, false);
        var afterRevoke = await NewService().CreateAsync(UserId, "Laptop", null);

        Assert.Multiple(() =>
        {
            Assert.That(duplicate.ErrorMessage, Is.EqualTo("You already have a token named 'Laptop'."));
            Assert.That(otherUser.Success, Is.True, "names are per user");
            Assert.That(afterRevoke.Success, Is.True);
        });
    }

    [Test]
    public async Task CreateAsync_NameOfAnExpiredToken_IsFree_AndTheExpiredTokenIsRevoked()
    {
        var old = await NewService().CreateAsync(UserId, "Laptop", DateTime.UtcNow.AddDays(1));
        await UpdateAsync(old.Token!.Id, t => t.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1));

        var renewed = await NewService().CreateAsync(UserId, "Laptop", null);

        Assert.Multiple(async () =>
        {
            Assert.That((renewed.Success, renewed.ErrorMessage), Is.EqualTo((true, (string?)null)));
            Assert.That((await StoredAsync(old.Token.Id)).RevokedUtc, Is.Not.Null);
        });
    }

    [Test]
    public async Task CreateAsync_SaveRacesAConcurrentCreate_ReturnsTheNameTakenError()
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(_db)
            .AddInterceptors(new FailingSaveInterceptor())
            .Options;
        await using var ctx = new CmsDbContext(options);

        var result = await new ApiTokenService(ctx).CreateAsync(UserId, "Laptop", null);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo((false, (string?)"You already have a token named 'Laptop'.", (string?)null, (ApiTokenDTO?)null)));
            Assert.That(ctx.ChangeTracker.Entries(), Is.Empty, "the failed insert isn't left tracked");
        });
    }

    /// <summary>Stands in for the unique-index violation the InMemory provider can't raise.</summary>
    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("duplicate key value violates unique constraint");
    }

    // --- ValidateAsync ---

    [Test]
    public async Task ValidateAsync_LiveToken_ReturnsIt_AndRecordsTheUse()
    {
        var created = await NewService().CreateAsync(UserId, "Laptop", DateTime.UtcNow.AddDays(1));

        var token = await NewService().ValidateAsync(created.PlainToken);

        Assert.Multiple(async () =>
        {
            Assert.That(token?.Id, Is.EqualTo(created.Token!.Id));
            Assert.That((await StoredAsync(created.Token.Id)).LastUsedUtc, Is.Not.Null);
        });
    }

    [Test]
    public async Task ValidateAsync_RecentUse_IsNotRewritten_ButAnOlderOneIs()
    {
        var created = await NewService().CreateAsync(UserId, "Laptop", null);
        var recent = DateTime.UtcNow.AddSeconds(-10);
        await UpdateAsync(created.Token!.Id, t => t.LastUsedUtc = recent);

        await NewService().ValidateAsync(created.PlainToken);
        var afterRecent = (await StoredAsync(created.Token.Id)).LastUsedUtc;

        var old = DateTime.UtcNow - ApiTokenService.LastUsedResolution - TimeSpan.FromSeconds(1);
        await UpdateAsync(created.Token.Id, t => t.LastUsedUtc = old);
        await NewService().ValidateAsync(created.PlainToken);
        var afterOld = (await StoredAsync(created.Token.Id)).LastUsedUtc;

        Assert.Multiple(() =>
        {
            Assert.That(afterRecent, Is.EqualTo(recent));
            Assert.That(afterOld, Is.GreaterThan(old + ApiTokenService.LastUsedResolution));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("wwc_unknown")]
    public async Task ValidateAsync_MissingOrUnknown_ReturnsNull(string? plain)
    {
        await NewService().CreateAsync(UserId, "Laptop", null);

        Assert.That(await NewService().ValidateAsync(plain), Is.Null);
    }

    [Test]
    public async Task ValidateAsync_RevokedOrExpired_ReturnsNull()
    {
        var revoked = await NewService().CreateAsync(UserId, "Revoked", null);
        await NewService().RevokeAsync(revoked.Token!.Id, UserId, false);
        var expired = await NewService().CreateAsync(UserId, "Expired", DateTime.UtcNow.AddDays(1));
        await UpdateAsync(expired.Token!.Id, t => t.ExpiresUtc = DateTime.UtcNow.AddSeconds(-1));

        Assert.Multiple(async () =>
        {
            Assert.That(await NewService().ValidateAsync(revoked.PlainToken), Is.Null);
            Assert.That(await NewService().ValidateAsync(expired.PlainToken), Is.Null);
        });
    }

    // --- listing ---

    [Test]
    public async Task ListForUserAsync_ReturnsOnlyThatUsersTokens_NewestFirst()
    {
        var older = await NewService().CreateAsync(UserId, "Older", null);
        await UpdateAsync(older.Token!.Id, t => t.CreatedUtc = DateTime.UtcNow.AddDays(-1));
        var newer = await NewService().CreateAsync(UserId, "Newer", null);
        await NewService().CreateAsync(OtherUserId, "Theirs", null);

        var list = await NewService().ListForUserAsync(UserId);

        Assert.That(list.Select(t => t.Id), Is.EqualTo(new[] { newer.Token!.Id, older.Token.Id }));
    }

    [Test]
    public async Task ListAllAsync_IncludesEveryUsersTokens_WithTheirEmail()
    {
        var mine = await NewService().CreateAsync(UserId, "Mine", null);
        await UpdateAsync(mine.Token!.Id, t => t.CreatedUtc = DateTime.UtcNow.AddDays(-1));
        await NewService().CreateAsync(OtherUserId, "Theirs", null);

        var list = await NewService().ListAllAsync();

        Assert.That(list.Select(i => (i.Token.Name, i.UserEmail)),
            Is.EqualTo(new[] { ("Theirs", (string?)"b@example.com"), ("Mine", (string?)"a@example.com") }));
    }

    // --- RevokeAsync ---

    [Test]
    public async Task RevokeAsync_Owner_RevokesOnce()
    {
        var created = await NewService().CreateAsync(UserId, "Laptop", null);

        var first = await NewService().RevokeAsync(created.Token!.Id, UserId, false);
        var revokedAt = (await StoredAsync(created.Token.Id)).RevokedUtc;
        var again = await NewService().RevokeAsync(created.Token.Id, UserId, false);

        Assert.Multiple(async () =>
        {
            Assert.That(first, Is.EqualTo((true, (string?)null)));
            Assert.That(again, Is.EqualTo((true, (string?)null)));
            Assert.That(revokedAt, Is.Not.Null);
            Assert.That((await StoredAsync(created.Token.Id)).RevokedUtc, Is.EqualTo(revokedAt), "a second revoke changes nothing");
        });
    }

    [Test]
    public async Task RevokeAsync_AnotherUsersToken_IsNotFound_UnlessAdmin()
    {
        var created = await NewService().CreateAsync(UserId, "Laptop", null);

        var asOther = await NewService().RevokeAsync(created.Token!.Id, OtherUserId, false);
        var asAdmin = await NewService().RevokeAsync(created.Token.Id, OtherUserId, true);
        var missing = await NewService().RevokeAsync(Guid.NewGuid(), UserId, true);

        Assert.Multiple(() =>
        {
            Assert.That(asOther, Is.EqualTo((false, (string?)"Token not found.")));
            Assert.That(asAdmin, Is.EqualTo((true, (string?)null)));
            Assert.That(missing, Is.EqualTo((false, (string?)"Token not found.")));
        });
    }

    // --- UpsertSeedTokenAsync ---

    [Test]
    public async Task UpsertSeedTokenAsync_CreatesThenIsIdempotent()
    {
        var first = await NewService().UpsertSeedTokenAsync(UserId, "Dev", SeedPlain);
        var second = await NewService().UpsertSeedTokenAsync(UserId, "Dev", SeedPlain);

        var token = await NewService().ValidateAsync(SeedPlain);
        var all = await NewService().ListForUserAsync(UserId);
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo((true, (string?)null)));
            Assert.That(second, Is.EqualTo((true, (string?)null)));
            Assert.That(token?.Name, Is.EqualTo("Dev"));
            Assert.That(all, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task UpsertSeedTokenAsync_RevokedExpiredOrChanged_IsRepointedAndRevived()
    {
        await NewService().UpsertSeedTokenAsync(UserId, "Dev", SeedPlain);
        var id = (await NewService().ListForUserAsync(UserId)).Single().Id;
        await UpdateAsync(id, t => { t.RevokedUtc = DateTime.UtcNow; t.ExpiresUtc = DateTime.UtcNow.AddDays(1); });

        await NewService().UpsertSeedTokenAsync(UserId, "Dev", SeedPlain);
        var revived = await StoredAsync(id);
        await NewService().UpsertSeedTokenAsync(UserId, "Dev", SeedPlain + "-changed");

        Assert.Multiple(async () =>
        {
            Assert.That((revived.RevokedUtc, revived.ExpiresUtc), Is.EqualTo(((DateTime?)null, (DateTime?)null)));
            Assert.That(await NewService().ValidateAsync(SeedPlain), Is.Null, "the old plaintext no longer works");
            Assert.That((await NewService().ValidateAsync(SeedPlain + "-changed"))?.Id, Is.EqualTo(id));
        });
    }

    [Test]
    public async Task UpsertSeedTokenAsync_PrefersTheLiveTokenOverARevokedOneOfTheSameName()
    {
        var revoked = await NewService().CreateAsync(UserId, "Dev", null);
        await NewService().RevokeAsync(revoked.Token!.Id, UserId, false);
        await UpdateAsync(revoked.Token.Id, t => t.CreatedUtc = DateTime.UtcNow.AddDays(-1));
        var live = await NewService().CreateAsync(UserId, "Dev", null);

        await NewService().UpsertSeedTokenAsync(UserId, "Dev", SeedPlain);

        Assert.Multiple(async () =>
        {
            Assert.That((await NewService().ValidateAsync(SeedPlain))?.Id, Is.EqualTo(live.Token!.Id));
            Assert.That((await StoredAsync(revoked.Token.Id)).RevokedUtc, Is.Not.Null, "the revoked token stays revoked");
        });
    }

    [Test]
    public async Task UpsertSeedTokenAsync_RenamedSeed_RenamesTheTokenThatHoldsThePlaintext()
    {
        await NewService().UpsertSeedTokenAsync(UserId, "Integration host", SeedPlain);
        var id = (await NewService().ListForUserAsync(UserId)).Single().Id;
        await NewService().RevokeAsync(id, UserId, false);

        var renamed = await NewService().UpsertSeedTokenAsync(UserId, "CI", SeedPlain);

        var all = await NewService().ListForUserAsync(UserId);
        Assert.Multiple(() =>
        {
            Assert.That(renamed, Is.EqualTo((true, (string?)null)));
            Assert.That(all, Has.Count.EqualTo(1), "no second row with the same hash");
            Assert.That((all[0].Id, all[0].Name, all[0].RevokedUtc), Is.EqualTo((id, "CI", (DateTime?)null)));
        });
    }

    [Test]
    public async Task UpsertSeedTokenAsync_RenamedOntoALiveName_RevokesTheTokenThatHeldIt()
    {
        var holder = await NewService().CreateAsync(UserId, "CI", null);
        await NewService().UpsertSeedTokenAsync(UserId, "Integration host", SeedPlain);

        await NewService().UpsertSeedTokenAsync(UserId, "CI", SeedPlain);

        Assert.Multiple(async () =>
        {
            Assert.That((await NewService().ValidateAsync(SeedPlain))?.Name, Is.EqualTo("CI"));
            Assert.That((await StoredAsync(holder.Token!.Id)).RevokedUtc, Is.Not.Null);
        });
    }

    [Test]
    public async Task UpsertSeedTokenAsync_PlaintextOfAnotherUsersToken_IsRefused()
    {
        await NewService().UpsertSeedTokenAsync(OtherUserId, "Dev", SeedPlain);

        var result = await NewService().UpsertSeedTokenAsync(UserId, "Dev", SeedPlain);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo((false, (string?)"That seed token is already issued to another user.")));
            Assert.That(await NewService().ListForUserAsync(UserId), Is.Empty);
        });
    }

    [Test]
    public async Task UpsertSeedTokenAsync_InvalidInput_IsRefused()
    {
        var noUser = await NewService().UpsertSeedTokenAsync(" ", "Dev", SeedPlain);
        var noName = await NewService().UpsertSeedTokenAsync(UserId, "", SeedPlain);
        var weak = await NewService().UpsertSeedTokenAsync(UserId, "Dev", "short");
        var empty = await NewService().UpsertSeedTokenAsync(UserId, "Dev", null!);

        Assert.Multiple(() =>
        {
            Assert.That(noUser.ErrorMessage, Is.EqualTo("A user is required."));
            Assert.That(noName.ErrorMessage, Is.EqualTo("A token name is required."));
            Assert.That(weak.ErrorMessage, Does.Contain($"at least {ApiTokenRules.MinSeedTokenLength}"));
            Assert.That(empty.ErrorMessage, Does.Contain("at least"));
        });
    }
}
