using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.ContentBlock;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class ContentBlockModelTests
{
	private IContentService<ContentBlockDTO> _service = null!;
	private IMapper _mapper = null!;
	private ContentBlockModel _model = null!;

	[SetUp]
	public void SetUp()
	{
		_service = Substitute.For<IContentService<ContentBlockDTO>>();
		_mapper = TestSupport.CreateMapper();
		_model = new ContentBlockModel(_service, _mapper);
	}

	private static ContentBlockDTO Dto(Guid? id = null, Guid master = default, int version = 0, string title = "T")
	{
		var cid = id ?? Guid.NewGuid();
		return new ContentBlockDTO
		{
			ContentId = cid,
			Content = "c",
			ContentMeta = new ContentDTO { Id = cid, MasterId = master, Version = version, Title = title }
		};
	}

	[Test]
	public void Constructor_NullArguments_Throw()
	{
		Assert.Multiple(() =>
		{
			Assert.That(() => new ContentBlockModel(null!, _mapper), Throws.ArgumentNullException);
			Assert.That(() => new ContentBlockModel(_service, null!), Throws.ArgumentNullException);
		});
	}

	[Test]
	public void Metadata_HasExpectedValues()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_model.ContentType, Is.EqualTo("contentblocks"));
			Assert.That(_model.DisplayName, Is.EqualTo("Content Block"));
			Assert.That(_model.SupportsVersionHistory, Is.True);
			Assert.That(_model.WriteRoles, Is.Null);
			Assert.That(_model.HasSecondaryApiList, Is.False);
			Assert.That(_model.RegistryHandler, Is.Null);
			Assert.That(_model.ChildHandler, Is.Null);
		});
	}

	[Test]
	public async Task GetViewModelByMasterIdAsync_FoundAndNotFound()
	{
		var dto = Dto();
		_service.GetByMasterIdAsync(dto.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns(dto, (ContentBlockDTO?)null);

		Assert.Multiple(async () =>
		{
			Assert.That(await _model.GetViewModelByMasterIdAsync(dto.ContentMeta.MasterId), Is.Not.Null);
			Assert.That(await _model.GetViewModelByMasterIdAsync(dto.ContentMeta.MasterId), Is.Null);
		});
	}

	[Test]
	public async Task GetContentBlockIndexAsync_MapsAll()
	{
		_service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ContentBlockDTO> { Dto(), Dto() });

		var vm = await _model.GetContentBlockIndexAsync();

		Assert.That(vm.ContentBlocks, Has.Count.EqualTo(2));
	}

	[Test]
	public async Task GetUpsertModelAsync_NullId_ReturnsEmpty()
	{
		Assert.That(await _model.GetUpsertModelAsync(null), Is.Not.Null);
		Assert.That(await _model.GetUpsertModelAsync(Guid.Empty), Is.Not.Null);
	}

	[Test]
	public async Task GetUpsertModelAsync_FoundAndNotFound()
	{
		var dto = Dto();
		_service.GetByIdAsync(dto.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(dto);
		_service.GetByIdAsync(Arg.Is<Guid>(g => g != dto.ContentMeta.Id), Arg.Any<CancellationToken>()).Returns((ContentBlockDTO?)null);

		Assert.Multiple(async () =>
		{
			Assert.That(await _model.GetUpsertModelAsync(dto.ContentMeta.Id), Is.Not.Null);
			Assert.That(await _model.GetUpsertModelAsync(Guid.NewGuid()), Is.Null);
		});
	}

	[Test]
	public void SaveUpsertAsync_NullModel_Throws()
	{
		Assert.That(async () => await _model.SaveUpsertAsync((ContentBlockUpsertViewModel)null!), Throws.ArgumentNullException);
	}

	[Test]
	public async Task SaveUpsertAsync_SuccessAndFailure()
	{
		_service.UpsertAsync(Arg.Any<ContentBlockDTO>(), Arg.Any<CancellationToken>()).Returns(true, false);

		Assert.Multiple(async () =>
		{
			Assert.That((await _model.SaveUpsertAsync(new ContentBlockUpsertViewModel { Title = "T", Content = "c" })).Success, Is.True);
			Assert.That((await _model.SaveUpsertAsync(new ContentBlockUpsertViewModel { Title = "T", Content = "c" })).Success, Is.False);
		});
	}

	[Test]
	public async Task SaveUpsertAsync_ObjectOverload_WrapsResult()
	{
		_service.UpsertAsync(Arg.Any<ContentBlockDTO>(), Arg.Any<CancellationToken>()).Returns(true, false);

		var ok = await _model.SaveUpsertAsync((object)new ContentBlockUpsertViewModel { Title = "T", Content = "c" });
		var fail = await _model.SaveUpsertAsync((object)new ContentBlockUpsertViewModel { Title = "T", Content = "c" });

		Assert.Multiple(() =>
		{
			Assert.That(ok.Success, Is.True);
			Assert.That(fail.Success, Is.False);
		});
	}

	[Test]
	public async Task DeleteAsync_DelegatesToService()
	{
		_service.DeleteAsync(Arg.Any<Guid>(), false, true, Arg.Any<CancellationToken>()).Returns(true);

		Assert.That(await _model.DeleteAsync(Guid.NewGuid()), Is.True);
	}

	[Test]
	public async Task VersionHistory_BuildsWhenVersionsExistAndNullWhenNot()
	{
		var master = Guid.NewGuid();
		_service.GetAllVersionsAsync(master, Arg.Any<CancellationToken>())
			.Returns(new List<ContentBlockDTO> { Dto(master: master, version: 1), Dto(master: master, version: 0) },
				new List<ContentBlockDTO>());

		Assert.Multiple(async () =>
		{
			Assert.That(await _model.GetVersionHistoryAsync(master), Is.Not.Null);
			Assert.That(await _model.GetVersionHistoryViewModelAsync(master), Is.Null);
		});
	}

	[Test]
	public async Task GetUpsertModelForRestore_Variants()
	{
		var historical = Dto(version: 1);
		var latest = Dto(master: historical.ContentMeta.MasterId, version: 5);
		_service.GetByIdAsync(historical.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(historical);
		_service.GetByMasterIdAsync(historical.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns(latest);

		var vm = await _model.GetUpsertModelForRestoreAsync(historical.ContentMeta.Id);

		Assert.Multiple(() =>
		{
			Assert.That(vm, Is.Not.Null);
			Assert.That(vm!.Id, Is.EqualTo(latest.ContentMeta.Id));
			Assert.That(vm.Version, Is.EqualTo(5));
		});
	}

	[Test]
	public async Task GetUpsertModelForRestore_NullWhenHistoricalOrLatestMissing()
	{
		_service.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ContentBlockDTO?)null);
		Assert.That(await _model.GetUpsertModelForRestoreAsync(Guid.NewGuid()), Is.Null);

		var historical = Dto();
		_service.GetByIdAsync(historical.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(historical);
		_service.GetByMasterIdAsync(historical.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns((ContentBlockDTO?)null);
		Assert.That(await _model.GetUpsertModelForRestoreAsync(historical.ContentMeta.Id), Is.Null);
	}

	[Test]
	public async Task DeleteVersionAsync_DelegatesToService()
	{
		_service.DeleteAsync(Arg.Any<Guid>(), false, false, Arg.Any<CancellationToken>()).Returns(true);

		Assert.That(await _model.DeleteVersionAsync(Guid.NewGuid()), Is.True);
	}

	[Test]
	public async Task BaseHandlerDefaults_SecondaryApiListIsEmpty()
	{
		// ContentBlockModel does not override the base GetSecondaryApiListAsync.
		Assert.That(await _model.GetSecondaryApiListAsync("anything"), Is.Empty);
	}

	[Test]
	public async Task AdminHandler_IndexUpsertCreateApiAndRestore()
	{
		_service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ContentBlockDTO> { Dto() });
		var dto = Dto();
		_service.GetByIdAsync(dto.ContentMeta.Id, Arg.Any<CancellationToken>()).Returns(dto);
		_service.GetByMasterIdAsync(dto.ContentMeta.MasterId, Arg.Any<CancellationToken>()).Returns(dto);

		var emptyQuery = new MvcHarness().NewHttpContext(Array.Empty<string>()).Request.Query;

		Assert.Multiple(async () =>
		{
			Assert.That(await _model.GetIndexViewModelAsync(), Is.InstanceOf<ContentBlockIndexViewModel>());
			Assert.That(await _model.GetUpsertViewModelAsync(dto.ContentMeta.Id, emptyQuery), Is.Not.Null);
			Assert.That(_model.CreateEmptyUpsertViewModel(), Is.InstanceOf<ContentBlockUpsertViewModel>());
			Assert.That(await _model.GetApiListAsync(), Is.Not.Null);
			Assert.That(await _model.GetRestoreVersionViewModelAsync(dto.ContentMeta.Id), Is.Not.Null);
		});
	}
}
