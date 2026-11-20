using Microsoft.AspNetCore.Http;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Data.Services.ContentTypes;
using WebWayCMS.Models.ContentTypeRegistration;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class ContentTypeRegistrationModelTests
{
    private IContentStore<ContentTypeRegistrationDTO> _store = null!;
    private IContentTypeRegistry _contentTypes = null!;
    private IChangeSetScope _changeSetScope = null!;
    private ContentTypeRegistrationModel _model = null!;

    [SetUp]
    public void SetUp()
    {
        _store = Substitute.For<IContentStore<ContentTypeRegistrationDTO>>();
        _contentTypes = new ContentTypeRegistry([
            new ContentTypeDescriptor("pages", typeof(PageDTO)),
            new ContentTypeDescriptor("articles", typeof(ArticleDTO)),
        ]);
        _changeSetScope = Substitute.For<IChangeSetScope>();
        _model = new ContentTypeRegistrationModel(_store, _contentTypes, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes());
    }

    private static ContentTypeRegistrationDTO Dto(
        string key = "pages",
        string displayName = "Page",
        string category = "General",
        int order = 0,
        Guid? nodeId = null,
        int version = 1,
        ContentVersionState state = ContentVersionState.Draft) => new()
    {
        VersionId = Guid.NewGuid(),
        Version = new ContentVersion
        {
            Node = new ContentNode { Id = nodeId ?? Guid.NewGuid() },
            Title = displayName,
            Slug = key,
            VersionNumber = version,
            State = state,
        },
        ContentTypeKey = key,
        ClrTypeName = "Clr." + key,
        DisplayName = displayName,
        PluralDisplayName = displayName + "s",
        Description = "About " + key,
        Category = category,
        IconClass = "fas fa-file",
        Order = order,
    };

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ContentTypeRegistrationModel(null!, _contentTypes, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()),
                Throws.ArgumentNullException);
            Assert.That(() => new ContentTypeRegistrationModel(_store, null!, _changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes()),
                Throws.ArgumentNullException);
        });
    }

    [Test]
    public void Metadata_HasExpectedValues()
    {
        IAdminCrudHandler handler = _model;
        Assert.Multiple(() =>
        {
            Assert.That(_model.ContentType, Is.EqualTo("contenttypes"));
            Assert.That(_model.DisplayName, Is.EqualTo("Content Type"));
            Assert.That(_model.IndexViewPath, Is.EqualTo("~/Views/ContentTypeRegistration/Index.cshtml"));
            Assert.That(_model.UpsertViewPath, Is.EqualTo("~/Views/ContentTypeRegistration/ContentTypeRegistrationUpsert.cshtml"));
            Assert.That(_model.WriteRoles, Is.Null);
            Assert.That(handler.IsContentType, Is.True);
            Assert.That(_model.CreateEmptyUpsertViewModel(), Is.TypeOf<ContentTypeRegistrationUpsertViewModel>());
        });
    }

    [Test]
    public async Task GetIndexViewModelAsync_OrdersByCategoryOrderName_AndFlagsOrphans()
    {
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns([
            Dto("articles", "Article", category: "Content", order: 2),
            Dto("gone", "Gone", category: "Content", order: 1),
            Dto("pages", "Page", category: "Content", order: 1),
            Dto("widgets", "Widget", category: "Advanced"),
        ]);

        var vm = (ContentTypeRegistrationIndexViewModel)await _model.GetIndexViewModelAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.Registrations.Select(r => r.ContentTypeKey), Is.EqualTo(new[] { "widgets", "gone", "pages", "articles" }));
            Assert.That(vm.OrphanedKeys, Is.EquivalentTo(new[] { "gone", "widgets" }));
            Assert.That(vm.OrphanedKeys.Contains("GONE"), Is.True);
        });
    }

    [Test]
    public async Task GetUpsertViewModelAsync_MapsTheCurrentDraft()
    {
        var nodeId = Guid.NewGuid();
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>())
            .Returns(Dto(nodeId: nodeId, version: 3, state: ContentVersionState.Published));

        var vm = (ContentTypeRegistrationUpsertViewModel?)await _model.GetUpsertViewModelAsync(nodeId, new QueryCollection());

        Assert.Multiple(() =>
        {
            Assert.That(vm!.NodeId, Is.EqualTo(nodeId));
            Assert.That(vm.ExpectedVersionNumber, Is.EqualTo(3));
            Assert.That(vm.Title, Is.EqualTo("Page"));
            Assert.That(vm.Slug, Is.EqualTo("pages"));
            Assert.That(vm.ContentTypeKey, Is.EqualTo("pages"));
            Assert.That(vm.ClrTypeName, Is.EqualTo("Clr.pages"));
            Assert.That(vm.DisplayName, Is.EqualTo("Page"));
            Assert.That(vm.PluralDisplayName, Is.EqualTo("Pages"));
            Assert.That(vm.Description, Is.EqualTo("About pages"));
            Assert.That(vm.Category, Is.EqualTo("General"));
            Assert.That(vm.IconClass, Is.EqualTo("fas fa-file"));
            Assert.That(vm.Order, Is.Zero);
            Assert.That(vm.IsPublished, Is.True);
        });
    }

    [Test]
    public async Task SaveUpsertAsync_Create_IsRefused()
    {
        var result = await _model.SaveUpsertAsync(new ContentTypeRegistrationUpsertViewModel
        {
            Title = "New", DisplayName = "New", PluralDisplayName = "News", Category = "General",
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(ContentTypeRegistrationModel.CreateRefusedMessage));
        });
        await _store.DidNotReceiveWithAnyArgs().SaveDraftAsync(default!, default);
    }

    [Test]
    public async Task SaveUpsertAsync_EmptyNodeId_IsRefusedAsACreate()
    {
        var result = await _model.SaveUpsertAsync(new ContentTypeRegistrationUpsertViewModel
        {
            NodeId = Guid.Empty, Title = "New", DisplayName = "New", PluralDisplayName = "News", Category = "General",
        });

        Assert.That(result.ErrorMessage, Is.EqualTo(ContentTypeRegistrationModel.CreateRefusedMessage));
    }

    [Test]
    public async Task SaveUpsertAsync_UnknownNode_Fails()
    {
        _store.GetCurrentDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ContentTypeRegistrationDTO?)null);

        var result = await _model.SaveUpsertAsync(new ContentTypeRegistrationUpsertViewModel
        {
            NodeId = Guid.NewGuid(), Title = "T", DisplayName = "D", PluralDisplayName = "Ds", Category = "General",
        });

        Assert.That(result.ErrorMessage, Is.EqualTo("Content type registration not found."));
    }

    [Test]
    public async Task SaveUpsertAsync_Edit_ChangesOnlyAdminFields()
    {
        var nodeId = Guid.NewGuid();
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(Dto(nodeId: nodeId, version: 2));
        ContentTypeRegistrationDTO? saved = null;
        _store.SaveDraftAsync(Arg.Do<ContentTypeRegistrationDTO>(d => saved = d), 2, Arg.Any<CancellationToken>())
            .Returns(new ContentWriteResult(true, NodeId: nodeId));

        var result = await _model.SaveUpsertAsync(new ContentTypeRegistrationUpsertViewModel
        {
            NodeId = nodeId,
            ExpectedVersionNumber = 2,
            ContentTypeKey = "hacked",
            ClrTypeName = "Hacked",
            Title = "Web Page",
            DisplayName = "Web Page",
            PluralDisplayName = "Web Pages",
            Category = "Content",
            Order = 5,
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(saved!.ContentTypeKey, Is.EqualTo("pages"));
            Assert.That(saved.ClrTypeName, Is.EqualTo("Clr.pages"));
            Assert.That(saved.Version.Title, Is.EqualTo("Web Page"));
            Assert.That(saved.Version.Slug, Is.Empty);
            Assert.That(saved.DisplayName, Is.EqualTo("Web Page"));
            Assert.That(saved.PluralDisplayName, Is.EqualTo("Web Pages"));
            Assert.That(saved.Description, Is.Empty);
            Assert.That(saved.Category, Is.EqualTo("Content"));
            Assert.That(saved.IconClass, Is.Empty);
            Assert.That(saved.Order, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task Delete_IsRefused_WithAReason()
    {
        var result = await _model.TryDeleteAsync(Guid.NewGuid());
        var plain = await _model.DeleteAsync(Guid.NewGuid());
        var self = await ((IContentDeleter)_model).DeleteSelfAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo((false, (string?)ContentTypeRegistrationModel.DeleteRefusedMessage, 0)));
            Assert.That(plain, Is.False);
            Assert.That(self, Is.False);
        });
        await _store.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
    }

    [Test]
    public async Task GetApiListAsync_TitlesByDisplayName()
    {
        _store.GetAllCurrentDraftsAsync(Arg.Any<CancellationToken>()).Returns([Dto(displayName: "Web Page")]);

        var list = (await _model.GetApiListAsync()).ToList();

        Assert.That(list.Single().ToString(), Does.Contain("Web Page"));
    }

    [Test]
    public async Task GetRestoreVersionViewModelAsync_MapsTheHistoricalVersion_WithTheCurrentVersionNumber()
    {
        var nodeId = Guid.NewGuid();
        var historicalId = Guid.NewGuid();
        _store.GetVersionAsync(historicalId, Arg.Any<CancellationToken>()).Returns(Dto(nodeId: nodeId, displayName: "Old", version: 1));
        _store.GetCurrentDraftAsync(nodeId, Arg.Any<CancellationToken>()).Returns(Dto(nodeId: nodeId, version: 4));

        var vm = (ContentTypeRegistrationUpsertViewModel?)await _model.GetRestoreVersionViewModelAsync(historicalId);

        Assert.Multiple(() =>
        {
            Assert.That(vm!.DisplayName, Is.EqualTo("Old"));
            Assert.That(vm.ExpectedVersionNumber, Is.EqualTo(4));
        });
    }
}
