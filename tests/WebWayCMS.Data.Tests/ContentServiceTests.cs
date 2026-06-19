using Microsoft.EntityFrameworkCore;

using NUnit.Framework;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class ContentServiceTests
{
	private string _db = null!;

	[SetUp]
	public void SetUp() => _db = TestContexts.NewDb();

	private ContentBlockContext NewContext() => TestContexts.ContentBlock(_db);

	private ContentService<ContentBlockDTO> NewService() => new(NewContext());

	private static ContentBlockDTO Block(Guid master, int version, string title = "t",
		bool published = true, bool deleted = false, Guid? parent = null, string slug = "",
		DateTime? mod = null)
	{
		var id = Guid.NewGuid();
		return new ContentBlockDTO
		{
			ContentId = id,
			Content = "c",
			ContentMeta = new ContentDTO
			{
				Id = id,
				MasterId = master,
				Version = version,
				Title = title,
				Slug = slug,
				IsPublished = published,
				IsDeleted = deleted,
				ParentMasterId = parent,
				ModificationDate = mod ?? DateTime.UtcNow
			}
		};
	}

	private static ContentBlockDTO Block(ContentDTO meta) => new()
	{
		ContentId = meta.Id,
		Content = "c",
		ContentMeta = meta
	};

	private async Task SeedAsync(params ContentBlockDTO[] blocks)
	{
		await using var ctx = NewContext();
		ctx.ContentBlocks.AddRange(blocks);
		await ctx.SaveChangesAsync();
	}

	[Test]
	public void Constructor_NullContext_Throws()
	{
		Assert.That(() => new ContentService<ContentBlockDTO>(null!), Throws.ArgumentNullException);
	}

	[Test]
	public async Task GetAllAsync_ReturnsLatestVersionsOrderedByModificationDesc()
	{
		var m1 = Guid.NewGuid();
		var m2 = Guid.NewGuid();
		await SeedAsync(
			Block(m1, 0, mod: new DateTime(2024, 1, 1)),
			Block(m1, 1, mod: new DateTime(2024, 6, 1)),
			Block(m2, 0, mod: new DateTime(2024, 3, 1)));

		var all = await NewService().GetAllAsync();

		Assert.Multiple(() =>
		{
			Assert.That(all, Has.Count.EqualTo(2));
			Assert.That(all[0].ContentMeta.MasterId, Is.EqualTo(m1)); // newest modification first
			Assert.That(all.All(b => b.ContentMeta.Version == (b.ContentMeta.MasterId == m1 ? 1 : 0)));
		});
	}

	[Test]
	public async Task GetByIdAsync_FoundAndNotFound()
	{
		var block = Block(Guid.NewGuid(), 0);
		await SeedAsync(block);

		Assert.Multiple(async () =>
		{
			Assert.That(await NewService().GetByIdAsync(block.ContentMeta.Id), Is.Not.Null);
			Assert.That(await NewService().GetByIdAsync(Guid.NewGuid()), Is.Null);
		});
	}

	[Test]
	public async Task GetByMasterIdAsync_ReturnsHighestVersion()
	{
		var m = Guid.NewGuid();
		await SeedAsync(Block(m, 0), Block(m, 2), Block(m, 1));

		var result = await NewService().GetByMasterIdAsync(m);

		Assert.That(result!.ContentMeta.Version, Is.EqualTo(2));
	}

	[Test]
	public async Task GetAllVersionsAsync_ReturnsAllOrderedDesc()
	{
		var m = Guid.NewGuid();
		await SeedAsync(Block(m, 0), Block(m, 1), Block(m, 2));

		var versions = await NewService().GetAllVersionsAsync(m);

		Assert.That(versions.Select(v => v.ContentMeta.Version), Is.EqualTo(new[] { 2, 1, 0 }));
	}

	[Test]
	public void CreateAsync_Null_Throws()
	{
		Assert.That(async () => await NewService().CreateAsync(null!), Throws.ArgumentNullException);
	}

	[Test]
	public async Task CreateAsync_EmptyId_AssignsIdAndMasterId_AndSlugFromTitle()
	{
		var entity = new ContentBlockDTO { Content = "c", ContentMeta = new ContentDTO { Title = "Hello World", IsPublished = true } };

		var created = await NewService().CreateAsync(entity);

		Assert.Multiple(() =>
		{
			Assert.That(created.ContentMeta.Id, Is.Not.EqualTo(Guid.Empty));
			Assert.That(created.ContentId, Is.EqualTo(created.ContentMeta.Id));
			Assert.That(created.ContentMeta.MasterId, Is.EqualTo(created.ContentMeta.Id));
			Assert.That(created.ContentMeta.Slug, Is.EqualTo(Uri.EscapeDataString("Hello World")));
			Assert.That(created.ContentMeta.PublicationDate, Is.Not.EqualTo(default(DateTime)));
			Assert.That(created.ContentMeta.CreationDate, Is.Not.EqualTo(default(DateTime)));
		});
	}

	[Test]
	public async Task CreateAsync_PresetIdAndSlug_AndUnpublished_AreRespected()
	{
		var id = Guid.NewGuid();
		var entity = new ContentBlockDTO { Content = "c", ContentMeta = new ContentDTO { Id = id, Title = "T", Slug = "custom", IsPublished = false } };

		var created = await NewService().CreateAsync(entity);

		Assert.Multiple(() =>
		{
			Assert.That(created.ContentMeta.Id, Is.EqualTo(id));
			Assert.That(created.ContentMeta.Slug, Is.EqualTo("custom"));
			Assert.That(created.ContentMeta.PublicationDate, Is.EqualTo(default(DateTime)));
		});
	}

	[Test]
	public async Task CreateAsync_EmptyTitle_LeavesSlugEmpty()
	{
		var created = await NewService().CreateAsync(new ContentBlockDTO { Content = "c", ContentMeta = new ContentDTO { Title = "" } });

		Assert.That(created.ContentMeta.Slug, Is.Empty);
	}

	[Test]
	public async Task CreateAsync_PublishedWithPresetPublicationDate_IsKept()
	{
		var when = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var created = await NewService().CreateAsync(new ContentBlockDTO { Content = "c", ContentMeta = new ContentDTO { Title = "T", IsPublished = true, PublicationDate = when } });

		Assert.That(created.ContentMeta.PublicationDate, Is.EqualTo(when));
	}

	[Test]
	public void UpdateAsync_Null_Throws()
	{
		Assert.That(async () => await NewService().UpdateAsync(null!), Throws.ArgumentNullException);
	}

	[Test]
	public async Task UpdateAsync_NonExistent_ReturnsFalse()
	{
		var result = await NewService().UpdateAsync(Block(Guid.NewGuid(), 0));

		Assert.That(result, Is.False);
	}

	[Test]
	public async Task UpdateAsync_Existing_CreatesNewVersionAndUnpublishesPrevious()
	{
		var m = Guid.NewGuid();
		var existing = Block(m, 0, published: true);
		await SeedAsync(existing);

		var update = Block(new ContentDTO { Id = existing.ContentMeta.Id, MasterId = m, Version = 0, Title = "T", IsPublished = true });
		var ok = await NewService().UpdateAsync(update);

		await using var verify = NewContext();
		var versions = await verify.ContentBlocks.Where(b => b.ContentMeta.MasterId == m).OrderBy(b => b.ContentMeta.Version).ToListAsync();
		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(versions, Has.Count.EqualTo(2));
			Assert.That(versions.Count(b => b.ContentMeta.IsPublished), Is.EqualTo(1), "only the new version stays published");
			Assert.That(versions.Single(b => b.ContentMeta.IsPublished).ContentMeta.Version, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task UpdateAsync_Unpublished_SkipsUnpublishStep_AndAutoSlug()
	{
		var m = Guid.NewGuid();
		var existing = Block(m, 0, published: false);
		await SeedAsync(existing);

		var update = Block(new ContentDTO { Id = existing.ContentMeta.Id, MasterId = m, Version = 0, Title = "Auto Slug", IsPublished = false, Slug = "" });
		var ok = await NewService().UpdateAsync(update);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(update.ContentMeta.Slug, Is.EqualTo(Uri.EscapeDataString("Auto Slug")));
			Assert.That(update.ContentMeta.Version, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task UpdateAsync_PublishedWithDefaultPublicationDate_SetsIt()
	{
		var m = Guid.NewGuid();
		var existing = Block(m, 0, published: false);
		await SeedAsync(existing);

		var update = Block(new ContentDTO { Id = existing.ContentMeta.Id, MasterId = m, Version = 0, Title = "T", IsPublished = true, PublicationDate = default });
		await NewService().UpdateAsync(update);

		Assert.That(update.ContentMeta.PublicationDate, Is.Not.EqualTo(default(DateTime)));
	}

	[Test]
	public void UpsertAsync_Null_Throws()
	{
		Assert.That(async () => await NewService().UpsertAsync(null!), Throws.ArgumentNullException);
	}

	[Test]
	public async Task UpsertAsync_EmptyId_Creates()
	{
		var ok = await NewService().UpsertAsync(new ContentBlockDTO { Content = "c", ContentMeta = new ContentDTO { Title = "T" } });

		Assert.That(ok, Is.True);
	}

	[Test]
	public async Task UpsertAsync_EmptyMasterId_Creates()
	{
		var ok = await NewService().UpsertAsync(new ContentBlockDTO { ContentId = Guid.NewGuid(), Content = "c", ContentMeta = new ContentDTO { Id = Guid.NewGuid(), MasterId = Guid.Empty, Title = "T" } });

		Assert.That(ok, Is.True);
	}

	[Test]
	public async Task UpsertAsync_ExistingIdAndMaster_Updates()
	{
		var m = Guid.NewGuid();
		var existing = Block(m, 0);
		await SeedAsync(existing);

		var ok = await NewService().UpsertAsync(Block(new ContentDTO { Id = existing.ContentMeta.Id, MasterId = m, Title = "T" }));

		Assert.That(ok, Is.True);
	}

	[Test]
	public async Task GetBySlugAsync_Whitespace_ReturnsNull()
	{
		Assert.That(await NewService().GetBySlugAsync("  "), Is.Null);
	}

	[Test]
	public async Task GetBySlugAsync_ReturnsLatestMatchingSlug()
	{
		var m = Guid.NewGuid();
		await SeedAsync(Block(m, 0, slug: "the-slug"), Block(m, 1, slug: "the-slug"));

		var result = await NewService().GetBySlugAsync("the-slug");

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.Not.Null);
			Assert.That(result!.ContentMeta.Version, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task GetBySlugAsync_NotFound_ReturnsNull()
	{
		Assert.That(await NewService().GetBySlugAsync("missing"), Is.Null);
	}

	[Test]
	public async Task GetChildrenAsync_ReturnsLatestChildVersions()
	{
		var parent = Guid.NewGuid();
		var childMaster = Guid.NewGuid();
		await SeedAsync(
			Block(childMaster, 0, parent: parent),
			Block(childMaster, 1, parent: parent),
			Block(Guid.NewGuid(), 0, parent: Guid.NewGuid()));

		var children = await NewService().GetChildrenAsync(parent);

		Assert.Multiple(() =>
		{
			Assert.That(children, Has.Count.EqualTo(1));
			Assert.That(children[0].ContentMeta.Version, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task GetRootsAsync_ReturnsLatestRootVersions()
	{
		var rootMaster = Guid.NewGuid();
		await SeedAsync(
			Block(rootMaster, 0),
			Block(rootMaster, 1),
			Block(Guid.NewGuid(), 0, parent: Guid.NewGuid()));

		var roots = await NewService().GetRootsAsync();

		Assert.Multiple(() =>
		{
			Assert.That(roots, Has.Count.EqualTo(1));
			Assert.That(roots[0].ContentMeta.Version, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task DeleteAsync_NotFound_ReturnsFalse()
	{
		Assert.That(await NewService().DeleteAsync(Guid.NewGuid()), Is.False);
	}

	[Test]
	public async Task DeleteAsync_HardDeleteSingle_RemovesEntity()
	{
		var block = Block(Guid.NewGuid(), 0);
		await SeedAsync(block);

		var ok = await NewService().DeleteAsync(block.ContentMeta.Id);

		await using var verify = NewContext();
		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(verify.ContentBlocks.Any(b => b.ContentId == block.ContentMeta.Id), Is.False);
		});
	}

	[Test]
	public async Task DeleteAsync_SoftDelete_MarksDeletedViaNewVersion()
	{
		var m = Guid.NewGuid();
		var block = Block(m, 0, published: true);
		await SeedAsync(block);

		var ok = await NewService().DeleteAsync(block.ContentMeta.Id, softDelete: true);

		await using var verify = NewContext();
		var latest = await verify.ContentBlocks.Where(b => b.ContentMeta.MasterId == m).OrderByDescending(b => b.ContentMeta.Version).FirstAsync();
		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(latest.ContentMeta.IsDeleted, Is.True);
			Assert.That(latest.ContentMeta.IsPublished, Is.False);
		});
	}

	[Test]
	public async Task DeleteAsync_DeleteHistoryHard_RemovesAllVersions()
	{
		var m = Guid.NewGuid();
		await SeedAsync(Block(m, 0), Block(m, 1));
		var anyId = (await NewService().GetByMasterIdAsync(m))!.ContentMeta.Id;

		var ok = await NewService().DeleteAsync(anyId, deleteHistory: true);

		await using var verify = NewContext();
		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(verify.ContentBlocks.Any(b => b.ContentMeta.MasterId == m), Is.False);
		});
	}

	[Test]
	public async Task DeleteAsync_DeleteHistorySoft_MarksAllVersionsDeleted()
	{
		var m = Guid.NewGuid();
		await SeedAsync(Block(m, 0, published: true), Block(m, 1, published: true));
		var anyId = (await NewService().GetByMasterIdAsync(m))!.ContentMeta.Id;

		var ok = await NewService().DeleteAsync(anyId, softDelete: true, deleteHistory: true);

		await using var verify = NewContext();
		var versions = await verify.ContentBlocks.Where(b => b.ContentMeta.MasterId == m).ToListAsync();
		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(versions.All(b => b.ContentMeta.IsDeleted && !b.ContentMeta.IsPublished), Is.True);
		});
	}
}
