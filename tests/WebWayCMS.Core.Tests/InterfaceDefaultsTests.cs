using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.ContentBlock;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class InterfaceDefaultsTests
{
    [Test]
    public async Task IAdminCrudHandler_DefaultMembers()
    {
        IAdminCrudHandler handler = new MinimalHandler();
        var query = new MvcHarness().NewHttpContext(Array.Empty<string>()).Request.Query;

        Assert.Multiple(async () =>
        {
            Assert.That(await handler.GetIndexViewModelAsync(query), Is.EqualTo("index"));
            Assert.That(handler.SupportsVersionHistory, Is.False);
            Assert.That(handler.SupportsPreview, Is.False);
            Assert.That(handler.SecondaryApiListKeys, Is.Empty);
            Assert.That(await handler.GetVersionHistoryViewModelAsync(Guid.NewGuid()), Is.Null);
            Assert.That(await handler.GetRestoreVersionViewModelAsync(Guid.NewGuid()), Is.Null);
            Assert.That(await handler.DeleteVersionAsync(Guid.NewGuid()), Is.False);
        });
    }

    [Test]
    public async Task IAdminCrudHandler_PublishingDefaults()
    {
        IAdminCrudHandler handler = new MinimalHandler();

        Assert.Multiple(async () =>
        {
            Assert.That(handler.SupportsPublishing, Is.True);
            Assert.That(handler.PublishRoles, Is.Null);
            Assert.That((await handler.PublishAsync(Guid.NewGuid())).Success, Is.False);
            Assert.That((await handler.PublishAsync(Guid.NewGuid())).ErrorMessage, Is.EqualTo("Publishing is not supported."));
            Assert.That((await handler.UnpublishAsync(Guid.NewGuid())).Success, Is.False);
            Assert.That((await handler.UnpublishAsync(Guid.NewGuid())).ErrorMessage, Is.EqualTo("Unpublishing is not supported."));
            Assert.That((await handler.RestoreVersionAsync(Guid.NewGuid())).Success, Is.False);
            Assert.That((await handler.RestoreVersionAsync(Guid.NewGuid())).ErrorMessage, Is.EqualTo("Restoring versions is not supported."));
        });
    }

    [Test]
    public async Task IAdminCrudHandler_ContainmentDefaults()
    {
        IAdminCrudHandler handler = new MinimalHandler();

        Assert.Multiple(async () =>
        {
            Assert.That(await handler.GetRelatedChildrenAsync("Parent", Guid.NewGuid()), Is.Empty);
            Assert.That(await handler.ReorderRelatedAsync("Parent", Guid.NewGuid(), []),
                Is.EqualTo((false, IAdminCrudHandler.ReorderNotSupportedMessage)));
            Assert.That(await handler.MoveRelatedAsync("Parent", Guid.NewGuid(), Guid.NewGuid(), 0, 1),
                Is.EqualTo((false, IAdminCrudHandler.MoveNotSupportedMessage)));
        });
    }

    [Test]
    public async Task IAdminCrudHandler_ApiListExcludingSubtree_DefaultsToThePlainList()
    {
        IAdminCrudHandler handler = new MinimalHandler();

        Assert.That(await handler.GetApiListExcludingSubtreeAsync(Guid.NewGuid()), Is.Empty);
    }

    [Test]
    public async Task AdminCrudModel_ApiListExcludingSubtree_DefaultsToThePlainList()
    {
        var model = new MinimalCrudModel(Substitute.For<IContentStore<ContentBlockDTO>>(), Substitute.For<IChangeSetScope>(), TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes());

        Assert.That(await model.GetApiListExcludingSubtreeAsync(Guid.NewGuid()), Is.Empty);
    }

    [Test]
    public async Task IAdminCrudHandler_RestoreWithExpectedVersion_DefaultsToThePlainRestore()
    {
        IAdminCrudHandler handler = new MinimalHandler();

        var result = await handler.RestoreVersionAsync(Guid.NewGuid(), 3);

        Assert.That(result.ErrorMessage, Is.EqualTo("Restoring versions is not supported."));
    }

    [Test]
    public void IAdminRegistryHandler_DefaultGetForm_ReturnsNotFound()
    {
        IAdminRegistryHandler handler = new MinimalRegistryHandler();
        var result = handler.GetForm("test", null);

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void IAdminRegistryHandler_DefaultDisplayModeGetForm_DelegatesToGetForm()
    {
        IAdminRegistryHandler handler = new MinimalRegistryHandler();
        var result = handler.GetForm("test", null, WebWayCMS.Attributes.WidgetDisplayMode.List);

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task AdminCrudModel_DefaultRestoreVersionViewModel_ReturnsNull()
    {
        var store = Substitute.For<IContentStore<ContentBlockDTO>>();
        var changeSetScope = Substitute.For<IChangeSetScope>();
        var model = new MinimalCrudModel(store, changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes());

        Assert.Multiple(async () =>
        {
            Assert.That(model.SupportsPreview, Is.False);
            Assert.That(model.SecondaryApiListKeys, Is.Empty);
            Assert.That(await model.GetRestoreVersionViewModelAsync(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public async Task IAdminCrudHandler_TryDeleteVersionAsync_Default()
    {
        IAdminCrudHandler refusing = new MinimalHandler();
        IAdminCrudHandler deleting = new DeletingMinimalHandler();

        Assert.Multiple(async () =>
        {
            Assert.That(await refusing.TryDeleteVersionAsync(Guid.NewGuid()), Is.EqualTo((false, IAdminCrudHandler.VersionNotDeletedMessage)));
            Assert.That(await deleting.TryDeleteVersionAsync(Guid.NewGuid()), Is.EqualTo((true, (string?)null)));
        });
    }

    [Test]
    public async Task AdminCrudModel_TryDeleteVersionAsync_ReportsTheOutcome()
    {
        var model = new MinimalCrudModel(Substitute.For<IContentStore<ContentBlockDTO>>(), Substitute.For<IChangeSetScope>(), TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes());

        var deleted = await model.TryDeleteVersionAsync(Guid.NewGuid());
        model.DeleteVersionResult = false;
        var refused = await model.TryDeleteVersionAsync(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.EqualTo((true, (string?)null)));
            Assert.That(refused, Is.EqualTo((false, IAdminCrudHandler.VersionNotDeletedMessage)));
        });
    }

    [Test]
    public async Task AdminCrudModel_SaveUpsert_InvalidModel_ShortCircuitsBeforeCore()
    {
        var store = Substitute.For<IContentStore<ContentBlockDTO>>();
        var changeSetScope = Substitute.For<IChangeSetScope>();
        var model = new MinimalCrudModel(store, changeSetScope, TestSupport.AcceptingBinder(), TestSupport.PassThroughDeletes());

        var result = await model.SaveUpsertAsync(new ContentBlockUpsertViewModel { Title = "T", Content = "" });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("Content"));
            Assert.That(model.SaveUpsertCoreCalled, Is.False);
        });
    }
}
