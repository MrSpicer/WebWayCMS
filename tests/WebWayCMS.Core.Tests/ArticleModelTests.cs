using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Article;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class ArticleModelTests
{
	private IContentService<ArticleDTO> _service = null!;
	private IMapper _mapper = null!;
	private ArticleModel _model = null!;

	[SetUp]
	public void SetUp()
	{
		_service = Substitute.For<IContentService<ArticleDTO>>();
		_mapper = TestSupport.CreateMapper();
		_model = new ArticleModel(_service, _mapper);
	}

	private static ArticleDTO Dto(Guid? id = null, Guid master = default, int version = 0)
	{
		var cid = id ?? Guid.NewGuid();
		return new ArticleDTO
		{
			ContentId = cid,
			Body = "b",
			ContentMeta = new ContentDTO { Id = cid, MasterId = master, Version = version, Title = "T", Slug = "s" }
		};
	}

	[Test]
	public void Constructor_NullArguments_Throw()
	{
		Assert.Multiple(() =>
		{
			Assert.That(() => new ArticleModel(null!, _mapper), Throws.ArgumentNullException);
			Assert.That(() => new ArticleModel(_service, null!), Throws.ArgumentNullException);
		});
	}

	[Test]
	public async Task GetPostViewModelAsync_FoundAndNotFound()
	{
		var dto = Dto();
		_service.GetByIdAsync(dto.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(dto, (ArticleDTO?)null);

		Assert.Multiple(async () =>
		{
			Assert.That(await _model.GetPostViewModelAsync(dto.ContentMeta.Id), Is.Not.Null);
			Assert.That(await _model.GetPostViewModelAsync(dto.ContentMeta.Id), Is.Null);
		});
	}

	[Test]
	public async Task GetBySlugAsync_FoundAndNotFound()
	{
		var dto = Dto();
		_service.GetBySlugAsync("s", Arg.Any<CancellationToken>()).Returns(dto, (ArticleDTO?)null);

		Assert.Multiple(async () =>
		{
			Assert.That(await _model.GetBySlugAsync("s"), Is.Not.Null);
			Assert.That(await _model.GetBySlugAsync("s"), Is.Null);
		});
	}

	[Test]
	public async Task GetUpsertViewModelAsync_NullIdReturnsEmpty_FoundAndNotFound()
	{
		var dto = Dto();
		_service.GetByIdAsync(dto.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(dto);
		_service.GetByIdAsync(Arg.Is<Guid>(g => g != dto.ContentMeta.Id), Arg.Any<CancellationToken>()).Returns((ArticleDTO?)null);

		Assert.Multiple(async () =>
		{
			Assert.That(await _model.GetUpsertViewModelAsync((Guid?)null), Is.Not.Null);
			Assert.That(await _model.GetUpsertViewModelAsync(dto.ContentMeta.Id), Is.Not.Null);
			Assert.That(await _model.GetUpsertViewModelAsync(Guid.NewGuid()), Is.Null);
		});
	}

	[Test]
	public async Task GetUpsertViewModelAsync_WithArticleList_NullIdSetsListId_FoundAndNotFound()
	{
		var listId = Guid.NewGuid();
		var dto = Dto();
		_service.GetByIdAsync(dto.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(dto);
		_service.GetByIdAsync(Arg.Is<Guid>(g => g != dto.ContentMeta.Id), Arg.Any<CancellationToken>()).Returns((ArticleDTO?)null);

		Assert.Multiple(async () =>
		{
			Assert.That((await _model.GetUpsertViewModelAsync(null, listId))!.ArticleListId, Is.EqualTo(listId));
			Assert.That(await _model.GetUpsertViewModelAsync(dto.ContentMeta.Id, listId), Is.Not.Null);
			Assert.That(await _model.GetUpsertViewModelAsync(Guid.NewGuid(), listId), Is.Null);
		});
	}

	[Test]
	public void SaveUpsertAsync_NullModel_Throws()
	{
		Assert.That(async () => await _model.SaveUpsertAsync(null!), Throws.ArgumentNullException);
	}

	[Test]
	public async Task SaveUpsertAsync_CreatesWhenNoId()
	{
		var result = await _model.SaveUpsertAsync(new ArticleUpsertViewModel { Id = null, Title = "T", Body = "b" });

		await _service.Received().CreateAsync(Arg.Any<ArticleDTO>(), Arg.Any<CancellationToken>());
		Assert.That(result.Success, Is.True);
	}

	[Test]
	public async Task SaveUpsertAsync_UpdatesWhenIdPresent_SuccessAndFailure()
	{
		_service.UpdateAsync(Arg.Any<ArticleDTO>(), Arg.Any<CancellationToken>()).Returns(true, false);

		Assert.Multiple(async () =>
		{
			Assert.That((await _model.SaveUpsertAsync(new ArticleUpsertViewModel { Id = Guid.NewGuid(), Title = "T", Body = "b" })).Success, Is.True);
			Assert.That((await _model.SaveUpsertAsync(new ArticleUpsertViewModel { Id = Guid.NewGuid(), Title = "T", Body = "b" })).Success, Is.False);
		});
	}

	[Test]
	public async Task DeleteAsync_DelegatesToService()
	{
		_service.DeleteAsync(Arg.Any<Guid>(), false, true, Arg.Any<CancellationToken>()).Returns(true);

		Assert.That(await _model.DeleteAsync(Guid.NewGuid()), Is.True);
	}

	[Test]
	public async Task GetVersionHistoryAsync_BuildsWithParentKey()
	{
		var master = Guid.NewGuid();
		_service.GetAllVersionsAsync(master, Arg.Any<CancellationToken>())
			.Returns(new List<ArticleDTO> { Dto(master: master, version: 0) });

		Assert.That(await _model.GetVersionHistoryAsync(master, "list-slug"), Is.Not.Null);
	}

	[Test]
	public async Task GetUpsertModelForRestore_Variants()
	{
		var historical = Dto(version: 1);
		var latest = Dto(master: historical.ContentMeta.MasterId, version: 3);
		_service.GetByIdAsync(historical.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(historical);
		_service.GetByMasterIdAsync(historical.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns(latest);

		var vm = await _model.GetUpsertModelForRestoreAsync(historical.ContentMeta.Id);

		Assert.That(vm!.Version, Is.EqualTo(3));
	}

	[Test]
	public async Task GetUpsertModelForRestore_NullWhenMissing()
	{
		_service.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ArticleDTO?)null);
		Assert.That(await _model.GetUpsertModelForRestoreAsync(Guid.NewGuid()), Is.Null);

		var historical = Dto();
		_service.GetByIdAsync(historical.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(historical);
		_service.GetByMasterIdAsync(historical.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns((ArticleDTO?)null);
		Assert.That(await _model.GetUpsertModelForRestoreAsync(historical.ContentMeta.Id), Is.Null);
	}

	[Test]
	public async Task DeleteVersionAsync_DelegatesToService()
	{
		_service.DeleteAsync(Arg.Any<Guid>(), false, false, Arg.Any<CancellationToken>()).Returns(true);

		Assert.That(await _model.DeleteVersionAsync(Guid.NewGuid()), Is.True);
	}
}
