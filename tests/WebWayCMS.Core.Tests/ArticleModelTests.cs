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
    private IContentStore<ArticleDTO> _store = null!;
    private IMapper _mapper = null!;
    private ArticleModel _model = null!;

    [SetUp]
    public void SetUp()
    {
        _store = Substitute.For<IContentStore<ArticleDTO>>();
        _mapper = TestSupport.CreateMapper();
        _model = new ArticleModel(_store, _mapper);
    }

    private static ArticleDTO Dto(Guid? nodeId = null, int version = 0, Guid? versionId = null)
    {
        var nid = nodeId ?? Guid.NewGuid();
        return new ArticleDTO
        {
            VersionId = versionId ?? Guid.NewGuid(),
            Body = "b",
            AuthorName = "a",
            Summary = "s",
            ArticleListNodeId = Guid.NewGuid(),
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = nid, CreatedUtc = DateTime.UtcNow },
                Title = "T",
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
            Assert.That(() => new ArticleModel(null!, _mapper), Throws.ArgumentNullException);
            Assert.That(() => new ArticleModel(_store, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task GetPostViewModelAsync_FoundAndNotFound()
    {
        var dto = Dto();
        _store.GetAsync(dto.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(dto, (ArticleDTO?)null);

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetPostViewModelAsync(dto.Version.Node.Id), Is.Not.Null);
            Assert.That(await _model.GetPostViewModelAsync(dto.Version.Node.Id), Is.Null);
        });
    }

    [Test]
    public async Task GetBySlugAsync_FoundAndNotFound()
    {
        var dto = Dto();
        _store.GetBySlugAsync("s", Arg.Any<CancellationToken>()).Returns(dto, (ArticleDTO?)null);

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
        _store.GetCurrentDraftAsync(dto.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(dto);
        _store.GetCurrentDraftAsync(Arg.Is<Guid>(g => g != dto.Version.Node.Id), Arg.Any<CancellationToken>()).Returns((ArticleDTO?)null);

        Assert.Multiple(async () =>
        {
            Assert.That(await _model.GetUpsertViewModelAsync((Guid?)null), Is.Not.Null);
            Assert.That(await _model.GetUpsertViewModelAsync(dto.Version.Node.Id), Is.Not.Null);
            Assert.That(await _model.GetUpsertViewModelAsync(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public async Task GetUpsertViewModelAsync_WithArticleList_NullIdSetsListId_FoundAndNotFound()
    {
        var listId = Guid.NewGuid();
        var dto = Dto();
        _store.GetCurrentDraftAsync(dto.Version.Node.Id, Arg.Any<CancellationToken>()).Returns(dto);
        _store.GetCurrentDraftAsync(Arg.Is<Guid>(g => g != dto.Version.Node.Id), Arg.Any<CancellationToken>()).Returns((ArticleDTO?)null);

        Assert.Multiple(async () =>
        {
            Assert.That((await _model.GetUpsertViewModelAsync(null, listId))!.ArticleListId, Is.EqualTo(listId));
            Assert.That(await _model.GetUpsertViewModelAsync(dto.Version.Node.Id, listId), Is.Not.Null);
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
        _store.SaveDraftAsync(Arg.Any<ArticleDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true));

        var result = await _model.SaveUpsertAsync(new ArticleUpsertViewModel { NodeId = null, Title = "T", Body = "b" });

        await _store.Received().SaveDraftAsync(Arg.Any<ArticleDTO>(), null, Arg.Any<CancellationToken>());
        await _store.Received().PublishAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task SaveUpsertAsync_UpdatesWhenIdPresent_SuccessAndFailure()
    {
        _store.SaveDraftAsync(Arg.Any<ArticleDTO>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true), new ContentWriteResult(false, "err"));

        Assert.Multiple(async () =>
        {
            Assert.That((await _model.SaveUpsertAsync(new ArticleUpsertViewModel { NodeId = Guid.NewGuid(), Title = "T", Body = "b" })).Success, Is.True);
            Assert.That((await _model.SaveUpsertAsync(new ArticleUpsertViewModel { NodeId = Guid.NewGuid(), Title = "T", Body = "b" })).Success, Is.False);
        });
    }

    [Test]
    public async Task SaveUpsertAsync_SanitizesRichTextBody()
    {
        ArticleDTO? saved = null;
        _store.SaveDraftAsync(Arg.Do<ArticleDTO>(d => saved = d), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, NodeId: Guid.NewGuid()));

        var result = await _model.SaveUpsertAsync(new ArticleUpsertViewModel
        {
            NodeId = null,
            Title = "T",
            Body = "<p onclick=\"alert(1)\">hello<script>alert(2)</script></p>"
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(saved!.Body, Does.Not.Contain("<script>"));
            Assert.That(saved.Body, Does.Not.Contain("onclick"));
        });
    }

    [Test]
    public async Task DeleteAsync_DelegatesToStore()
    {
        _store.DeleteAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(true);

        Assert.That(await _model.DeleteAsync(Guid.NewGuid()), Is.True);
    }

    [Test]
    public async Task GetVersionHistoryAsync_BuildsWithParentKey()
    {
        var nodeId = Guid.NewGuid();
        _store.GetAllVersionsAsync(nodeId, Arg.Any<CancellationToken>())
            .Returns(new List<ArticleDTO> { Dto(nodeId: nodeId, version: 0) });

        Assert.That(await _model.GetVersionHistoryAsync(nodeId, "list-slug"), Is.Not.Null);
    }

    [Test]
    public async Task GetUpsertModelForRestore_Variants()
    {
        var nodeId = Guid.NewGuid();
        var historical = Dto(nodeId: nodeId, version: 1);
        var latest = Dto(nodeId: nodeId, version: 3);
        _store.GetVersionAsync(historical.VersionId, Arg.Any<CancellationToken>()).Returns(historical);
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(latest);

        var vm = await _model.GetUpsertModelForRestoreAsync(historical.VersionId);

        Assert.That(vm!.ExpectedVersionNumber, Is.EqualTo(3));
    }

    [Test]
    public async Task GetUpsertModelForRestore_NullWhenMissing()
    {
        _store.GetVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ArticleDTO?)null);
        Assert.That(await _model.GetUpsertModelForRestoreAsync(Guid.NewGuid()), Is.Null);

        var nodeId = Guid.NewGuid();
        var historical = Dto(nodeId: nodeId);
        _store.GetVersionAsync(historical.VersionId, Arg.Any<CancellationToken>()).Returns(historical);
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns((ArticleDTO?)null);
        Assert.That(await _model.GetUpsertModelForRestoreAsync(historical.VersionId), Is.Null);
    }

    [Test]
    public async Task DeleteVersionAsync_DelegatesToStore()
    {
        _store.DeleteVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        Assert.That(await _model.DeleteVersionAsync(Guid.NewGuid()), Is.True);
    }
}
