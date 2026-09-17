using Microsoft.AspNetCore.Http;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Image;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class ImageModelTests
{
    private IContentStore<ImageDTO> _store = null!;
    private IChangeSetScope _changeSetScope = null!;
    private IMediaLibrary _library = null!;
    private IMapper _mapper = null!;
    private ImageModel _model = null!;

    private const string Hash = "hash-1";

    [SetUp]
    public void SetUp()
    {
        _store = Substitute.For<IContentStore<ImageDTO>>();
        _changeSetScope = Substitute.For<IChangeSetScope>();
        _library = Substitute.For<IMediaLibrary>();
        _mapper = TestSupport.CreateMapper();
        _model = new ImageModel(_store, _mapper, _library, _changeSetScope);
    }

    private static ImageDTO Dto(Guid? nodeId = null, int version = 0, string title = "T") => new()
    {
        VersionId = Guid.NewGuid(),
        BlobHash = Hash,
        AltText = "alt",
        Caption = "cap",
        Version = new ContentVersion
        {
            Node = new ContentNode { Id = nodeId ?? Guid.NewGuid(), CreatedUtc = DateTime.UtcNow },
            Title = title,
            Slug = "s",
            VersionNumber = version,
            State = ContentVersionState.Draft
        }
    };

    private static MediaBlobDTO Blob() => new()
    {
        Hash = Hash,
        ContentType = "image/png",
        Width = 12,
        Height = 34,
        OriginalFileName = "pic.png",
        ByteLength = 5,
        CreatedUtc = DateTime.UtcNow
    };

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ImageModel(null!, _mapper, _library, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new ImageModel(_store, null!, _library, _changeSetScope), Throws.ArgumentNullException);
            Assert.That(() => new ImageModel(_store, _mapper, null!, _changeSetScope), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void Metadata_IsExposedForGenericAdminDispatch()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_model.ContentType, Is.EqualTo("images"));
            Assert.That(_model.DisplayName, Is.EqualTo("Image"));
            Assert.That(_model.IndexViewPath, Does.Contain("AdminImage"));
            Assert.That(_model.UpsertViewPath, Does.Contain("ImageUpsert"));
            Assert.That(_model.SupportsPreview, Is.False);
        });
    }

    [Test]
    public async Task GetViewModelByNodeIdAsync_UnknownNode_ReturnsNull()
    {
        _store.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ImageDTO?)null);
        Assert.That(await _model.GetViewModelByNodeIdAsync(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task GetViewModelByNodeIdAsync_FillsDimensionsFromMediaCatalog()
    {
        var nodeId = Guid.NewGuid();
        _store.GetAsync(nodeId, Arg.Any<CancellationToken>()).Returns(Dto(nodeId));
        _library.GetMetadataAsync(Hash, Arg.Any<CancellationToken>()).Returns(Blob());

        var vm = await _model.GetViewModelByNodeIdAsync(nodeId);

        Assert.Multiple(() =>
        {
            Assert.That(vm!.Width, Is.EqualTo(12));
            Assert.That(vm.Height, Is.EqualTo(34));
            Assert.That(vm.ContentType, Is.EqualTo("image/png"));
            Assert.That(vm.Url, Is.EqualTo($"/media/{Hash}/pic.png"));
        });
    }

    [Test]
    public async Task GetViewModelByNodeIdAsync_MissingBlobMetadata_LeavesDimensionsUnset()
    {
        var nodeId = Guid.NewGuid();
        _store.GetAsync(nodeId, Arg.Any<CancellationToken>()).Returns(Dto(nodeId));
        _library.GetMetadataAsync(Hash, Arg.Any<CancellationToken>()).Returns((MediaBlobDTO?)null);

        var vm = await _model.GetViewModelByNodeIdAsync(nodeId);

        Assert.Multiple(() =>
        {
            Assert.That(vm!.Width, Is.Zero);
            Assert.That(vm.ContentType, Is.Empty);
        });
    }

    [Test]
    public async Task GetImageIndexAsync_MapsCurrentDrafts()
    {
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>())
            .Returns([Dto(title: "One"), Dto(title: "Two")]);

        var index = await _model.GetImageIndexAsync();

        Assert.That(index.Images.Select(i => i.Title), Is.EqualTo(new[] { "One", "Two" }));
    }

    [Test]
    public async Task GetUpsertModelAsync_NullOrEmptyNodeId_ReturnsEmptyModel()
    {
        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetUpsertModelAsync(null), Is.Not.Null);
            Assert.That(await _model.GetUpsertModelAsync(Guid.Empty), Is.Not.Null);
        });
    }

    [Test]
    public async Task GetUpsertModelAsync_UnknownNode_ReturnsNull()
    {
        _store.GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ImageDTO?)null);
        Assert.That(await _model.GetUpsertModelAsync(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task GetUpsertModelAsync_KnownNode_MapsFields()
    {
        var nodeId = Guid.NewGuid();
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(Dto(nodeId));

        var vm = await _model.GetUpsertModelAsync(nodeId);

        Assert.Multiple(() =>
        {
            Assert.That(vm!.BlobHash, Is.EqualTo(Hash));
            Assert.That(vm.AltText, Is.EqualTo("alt"));
            Assert.That(vm.Caption, Is.EqualTo("cap"));
        });
    }

    [Test]
    public void SaveUpsertAsync_NullModel_Throws() =>
        Assert.That(() => _model.SaveUpsertAsync((ImageUpsertViewModel)null!), Throws.ArgumentNullException);

    [Test]
    public async Task SaveUpsertAsync_HashNamingNoStoredBlob_Fails()
    {
        _library.GetMetadataAsync("ghost", Arg.Any<CancellationToken>()).Returns((MediaBlobDTO?)null);

        var (success, error) = await _model.SaveUpsertAsync(new ImageUpsertViewModel { BlobHash = "ghost", AltText = "a" });

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(error, Does.Contain("could not be found"));
        });
    }

    [Test]
    public async Task SaveUpsertAsync_StoreFailure_ReturnsStoreMessage()
    {
        _library.GetMetadataAsync(Hash, Arg.Any<CancellationToken>()).Returns(Blob());
        _store.SaveDraftAsync(Arg.Any<ImageDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false, "changed by someone else"));

        var (success, error) = await _model.SaveUpsertAsync(new ImageUpsertViewModel { BlobHash = Hash, AltText = "a" });

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(error, Is.EqualTo("changed by someone else"));
        });
    }

    [Test]
    public async Task SaveUpsertAsync_StoreFailureWithoutMessage_UsesFallback()
    {
        _library.GetMetadataAsync(Hash, Arg.Any<CancellationToken>()).Returns(Blob());
        _store.SaveDraftAsync(Arg.Any<ImageDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(false));

        var (_, error) = await _model.SaveUpsertAsync(new ImageUpsertViewModel { BlobHash = Hash, AltText = "a" });
        Assert.That(error, Does.Contain("error occurred while saving"));
    }

    [Test]
    public async Task SaveUpsertAsync_Success_WritesBackNodeId()
    {
        var nodeId = Guid.NewGuid();
        _library.GetMetadataAsync(Hash, Arg.Any<CancellationToken>()).Returns(Blob());
        _store.SaveDraftAsync(Arg.Any<ImageDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, null, Guid.NewGuid(), nodeId));

        var model = new ImageUpsertViewModel { BlobHash = Hash, AltText = "a" };
        var (success, _) = await _model.SaveUpsertAsync(model);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(model.NodeId, Is.EqualTo(nodeId));
        });
    }

    [Test]
    public async Task DeleteAsync_DelegatesHardDeleteToStore()
    {
        var nodeId = Guid.NewGuid();
        _store.DeleteAsync(nodeId, false, Arg.Any<CancellationToken>()).Returns(true);

        Assert.That(await _model.DeleteAsync(nodeId), Is.True);
    }

    [Test]
    public async Task DeleteVersionAsync_DelegatesToStore()
    {
        var id = Guid.NewGuid();
        _store.DeleteVersionAsync(id, Arg.Any<CancellationToken>()).Returns(true);

        Assert.That(await _model.DeleteVersionAsync(id), Is.True);
    }

    [Test]
    public async Task CreateEmptyUpsertViewModel_AndGenericDispatch_Work()
    {
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns([Dto()]);
        _store.GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ImageDTO?)null);

        Assert.Multiple(async () =>
        {
            Assert.That(_model.CreateEmptyUpsertViewModel(), Is.InstanceOf<ImageUpsertViewModel>());
            Assert.That(await _model.GetIndexViewModelAsync(), Is.InstanceOf<ImageIndexViewModel>());
            Assert.That(await _model.GetUpsertViewModelAsync(Guid.NewGuid(), new QueryCollection()), Is.Null);
        });
    }

    [Test]
    public async Task GetApiListAsync_ReturnsIdAndTitlePairs()
    {
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns([Dto(title: "Only")]);

        var list = (await _model.GetApiListAsync()).ToList();

        Assert.That(list, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SaveUpsertCore_ThroughChokePoint_ReturnsAdminSaveResult()
    {
        var nodeId = Guid.NewGuid();
        _library.GetMetadataAsync(Hash, Arg.Any<CancellationToken>()).Returns(Blob());
        _store.SaveDraftAsync(Arg.Any<ImageDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, null, Guid.NewGuid(), nodeId));
        _changeSetScope.Begin(Arg.Any<ChangeSetKind>(), Arg.Any<Guid?>(), Arg.Any<string?>())
            .Returns(Substitute.For<IDisposable>());

        var result = await _model.SaveUpsertAsync(
            (object)new ImageUpsertViewModel { BlobHash = Hash, AltText = "a", Title = "t" });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.NodeId, Is.EqualTo(nodeId));
        });
    }

    [Test]
    public async Task SaveUpsertAsync_MissingRequiredAltText_IsRejectedByValidation()
    {
        _changeSetScope.Begin(Arg.Any<ChangeSetKind>(), Arg.Any<Guid?>(), Arg.Any<string?>())
            .Returns(Substitute.For<IDisposable>());

        var result = await _model.SaveUpsertAsync(
            (object)new ImageUpsertViewModel { BlobHash = Hash, AltText = "", Title = "t" });

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task GetVersionHistoryAsync_BuildsHistoryFromStoredVersions()
    {
        var nodeId = Guid.NewGuid();
        _store.GetAllVersionsAsync(nodeId, Arg.Any<CancellationToken>())
            .Returns([Dto(nodeId, version: 1, title: "v1"), Dto(nodeId, version: 2, title: "v2")]);

        var history = await _model.GetVersionHistoryAsync(nodeId);

        Assert.Multiple(() =>
        {
            Assert.That(history, Is.Not.Null);
            Assert.That(history!.BackUrl, Is.EqualTo("/wadmin/images"));
            Assert.That(history.ContentType, Is.EqualTo("images"));
        });
    }

    [Test]
    public async Task GetRestoreVersionViewModelAsync_UnknownVersion_ReturnsNull()
    {
        _store.GetVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ImageDTO?)null);
        Assert.That(await _model.GetRestoreVersionViewModelAsync(Guid.NewGuid()), Is.Null);
    }
}
