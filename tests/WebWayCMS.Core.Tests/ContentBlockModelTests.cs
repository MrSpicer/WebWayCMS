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
    private IContentStore<ContentBlockDTO> _store = null!;
    private IChangeSetScope _changeSetScope = null!;
    private IMapper _mapper = null!;
    private ContentBlockModel _model = null!;

    [SetUp]
    public void SetUp()
    {
        _store = Substitute.For<IContentStore<ContentBlockDTO>>();
        _changeSetScope = Substitute.For<IChangeSetScope>();
        _mapper = TestSupport.CreateMapper();
        _model = new ContentBlockModel(_store, _mapper, _changeSetScope);
    }

    private static ContentBlockDTO Dto(Guid? nodeId = null, int version = 0, string title = "T")
    {
        var nid = nodeId ?? Guid.NewGuid();
        return new ContentBlockDTO
        {
            VersionId = Guid.NewGuid(),
            Content = "c",
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = nid, CreatedUtc = DateTime.UtcNow },
                Title = title,
                Slug = "s",
                VersionNumber = version,
                State = ContentVersionState.Draft
            }
        };
    }

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ContentBlockModel(null!, _mapper, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new ContentBlockModel(_store, null!, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new ContentBlockModel(_store, _mapper, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void Metadata_HasExpectedValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_model.ContentType, Is.EqualTo("contentblocks"));
            Assert.That(_model.DisplayName, Is.EqualTo("Content Block"));
            Assert.That(_model.IndexViewPath, Does.Contain("ContentBlocks.cshtml"));
            Assert.That(_model.UpsertViewPath, Does.Contain("ContentBlockUpsert.cshtml"));
            Assert.That(_model.SupportsVersionHistory, Is.True);
            Assert.That(_model.SupportsPublishing, Is.True);
            Assert.That(_model.WriteRoles, Is.Null);
            Assert.That(_model.PublishRoles, Is.Null);
            Assert.That(_model.HasSecondaryApiList, Is.False);
            Assert.That(_model.RegistryHandler, Is.Null);
            Assert.That(_model.ChildHandler, Is.Null);
        });
    }

    [Test]
    public async Task GetViewModelByNodeIdAsync_FoundAndNotFound()
    {
        var dto = Dto();
        _store.GetAsync(dto.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(dto, (ContentBlockDTO?)null);

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetViewModelByNodeIdAsync(dto.Version.Node.Id), Is.Not.Null);
            Assert.That(await _model.GetViewModelByNodeIdAsync(dto.Version.Node.Id), Is.Null);
        });
    }

    [Test]
    public async Task GetContentBlockIndexAsync_MapsAll()
    {
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<ContentBlockDTO> { Dto(), Dto() });

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
        _store.GetCurrentDraftAsync(dto.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(dto);
        _store.GetCurrentDraftAsync(Arg.Is<Guid>(g => g != dto.Version.Node.Id), Arg.Any<CancellationToken>()).Returns((ContentBlockDTO?)null);

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetUpsertModelAsync(dto.Version.Node.Id), Is.Not.Null);
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
        _store.SaveDraftAsync(Arg.Any<ContentBlockDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true), new ContentWriteResult(false, "err"));

        Assert.Multiple(async () =>
        {
            Assert.That((await _model.SaveUpsertAsync(new ContentBlockUpsertViewModel { Title = "T", Content = "c" })).Success, Is.True);
            Assert.That((await _model.SaveUpsertAsync(new ContentBlockUpsertViewModel { Title = "T", Content = "c" })).Success, Is.False);
        });
    }

    [Test]
    public async Task SaveUpsertAsync_ObjectOverload_WrapsResult()
    {
        _store.SaveDraftAsync(Arg.Any<ContentBlockDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true), new ContentWriteResult(false, "err"));

        var ok = await _model.SaveUpsertAsync((object)new ContentBlockUpsertViewModel { Title = "T", Content = "c" });
        var fail = await _model.SaveUpsertAsync((object)new ContentBlockUpsertViewModel { Title = "T", Content = "c" });

        Assert.Multiple(() =>
        {
            Assert.That(ok.Success, Is.True);
            Assert.That(fail.Success, Is.False);
        });
    }

    [Test]
    public async Task DeleteAsync_DelegatesToStore()
    {
        _store.DeleteAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(true);

        Assert.That(await _model.DeleteAsync(Guid.NewGuid()), Is.True);
    }

    [Test]
    public async Task VersionHistory_BuildsWhenVersionsExistAndNullWhenNot()
    {
        var nodeId = Guid.NewGuid();
        _store.GetAllVersionsAsync(nodeId, Arg.Any<CancellationToken>())
            .Returns(new List<ContentBlockDTO> { Dto(nodeId: nodeId, version: 1), Dto(nodeId: nodeId, version: 0) },
                new List<ContentBlockDTO>());

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetVersionHistoryAsync(nodeId), Is.Not.Null);
            Assert.That(await _model.GetVersionHistoryViewModelAsync(nodeId), Is.Null);
        });
    }

    [Test]
    public async Task DeleteVersionAsync_DelegatesToStore()
    {
        _store.DeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        Assert.That(await _model.DeleteVersionAsync(Guid.NewGuid()), Is.True);
    }

    [Test]
    public async Task PublishAsync_SuccessAndFailure()
    {
        _store.PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true), new ContentWriteResult(false, "err"));

        Assert.Multiple(async () =>
        {
            Assert.That((await _model.PublishAsync(Guid.NewGuid())).Success, Is.True);
            Assert.That((await _model.PublishAsync(Guid.NewGuid())).Success, Is.False);
        });
    }

    [Test]
    public async Task UnpublishAsync_SuccessAndFailure()
    {
        _store.UnpublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true), new ContentWriteResult(false, "err"));

        Assert.Multiple(async () =>
        {
            Assert.That((await _model.UnpublishAsync(Guid.NewGuid())).Success, Is.True);
            Assert.That((await _model.UnpublishAsync(Guid.NewGuid())).Success, Is.False);
        });
    }

    [Test]
    public async Task RestoreVersionAsync_SuccessAndFailure()
    {
        _store.RestoreAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true), new ContentWriteResult(false, "err"));

        Assert.Multiple(async () =>
        {
            Assert.That((await _model.RestoreVersionAsync(Guid.NewGuid())).Success, Is.True);
            Assert.That((await _model.RestoreVersionAsync(Guid.NewGuid())).Success, Is.False);
        });
    }

    [Test]
    public async Task BaseHandlerDefaults_SecondaryApiListIsEmpty()
    {
        Assert.That(await _model.GetSecondaryApiListAsync("anything"), Is.Empty);
    }

    [Test]
    public async Task AdminHandler_IndexUpsertCreateApiAndRestore()
    {
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns(new List<ContentBlockDTO> { Dto() });
        var dto = Dto(version: 5);
        var historical = Dto(nodeId: dto.Version.Node.Id, version: 2);
        _store.GetCurrentDraftAsync(dto.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(dto);
        _store.GetVersionAsync(historical.VersionId, Arg.Any<CancellationToken>()).Returns(historical);

        var emptyQuery = new MvcHarness().NewHttpContext(Array.Empty<string>()).Request.Query;

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetIndexViewModelAsync(), Is.InstanceOf<ContentBlockIndexViewModel>());
            Assert.That(await _model.GetUpsertViewModelAsync(dto.Version.Node.Id, emptyQuery), Is.Not.Null);
            Assert.That(_model.CreateEmptyUpsertViewModel(), Is.InstanceOf<ContentBlockUpsertViewModel>());
            Assert.That(await _model.GetApiListAsync(), Is.Not.Null);
            var restore = (ContentBlockUpsertViewModel)(await _model.GetRestoreVersionViewModelAsync(historical.VersionId))!;
            Assert.That(restore.ExpectedVersionNumber, Is.EqualTo(dto.Version.VersionNumber));
        });
    }
}
