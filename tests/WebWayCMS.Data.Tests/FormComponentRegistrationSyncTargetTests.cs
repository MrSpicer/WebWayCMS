using Microsoft.EntityFrameworkCore;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Data.Services.Registrations;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class FormComponentRegistrationSyncTargetTests
{
    private string _db = null!;

    [SetUp]
    public void SetUp() => _db = TestContexts.NewDb();

    private CmsDbContext NewContext() => TestContexts.Cms(_db);

    [Test]
    public async Task LoadRows_SaveCodeOwnedChanges_UpdatesInPlace()
    {
        await using (var ctx = NewContext())
        {
            ctx.Set<FormComponentRegistrationDTO>().Add(new FormComponentRegistrationDTO
            {
                Id = Guid.NewGuid(), ComponentName = "Text", ViewComponentName = "Old", DisplayName = "Text",
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            var target = new FormComponentRegistrationSyncTarget(ctx, new FormComponentRegistrationService(ctx));
            var row = (await target.LoadRowsAsync()).Single();
            Assert.That(target.KeyOf(row), Is.EqualTo("Text"));

            row.ViewComponentName = "FormText";
            await target.SaveCodeOwnedChangesAsync();
            Assert.That(ctx.ChangeTracker.Entries(), Is.Empty);
        }

        await using var check = NewContext();
        Assert.That((await check.Set<FormComponentRegistrationDTO>().SingleAsync()).ViewComponentName, Is.EqualTo("FormText"));
    }

    [Test]
    public async Task InsertAsync_GoesThroughTheService()
    {
        await using var ctx = NewContext();
        var target = new FormComponentRegistrationSyncTarget(ctx, new FormComponentRegistrationService(ctx));

        var first = await target.InsertAsync(new FormComponentRegistrationDTO { ComponentName = "Text", DisplayName = "Text" });
        var duplicate = await target.InsertAsync(new FormComponentRegistrationDTO { ComponentName = "Text", DisplayName = "Text" });

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo((true, (string?)null)));
            Assert.That(duplicate.Success, Is.False);
            Assert.That(duplicate.ErrorMessage, Does.Contain("already exists"));
        });
    }

    [Test]
    public void Ctor_NullArguments_Throw()
    {
        using var ctx = NewContext();
        var service = Substitute.For<IFormComponentRegistrationService>();

        Assert.Multiple(() =>
        {
            Assert.That(() => new FormComponentRegistrationSyncTarget(null!, service), Throws.ArgumentNullException);
            Assert.That(() => new FormComponentRegistrationSyncTarget(ctx, null!), Throws.ArgumentNullException);
        });
    }
}
