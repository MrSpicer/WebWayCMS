using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.ContentBlock;
using WebWayCMS.Models.Shared;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

/// <summary>
/// Minimal implementations that keep the interface default methods, so those default bodies
/// (defined in WebWayCMS.Core) are executed and counted.
/// </summary>
internal class MinimalHandler : IAdminCrudHandler
{
    public string ContentType => "minimal";
    public string DisplayName => "Minimal";
    public string[]? WriteRoles => null;
    public string IndexViewPath => "i";
    public string UpsertViewPath => "u";
    public Task<object> GetIndexViewModelAsync(CancellationToken ct = default) => Task.FromResult<object>("index");
    public Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default) => Task.FromResult<object?>(null);
    public object CreateEmptyUpsertViewModel() => new object();
    public Task<AdminSaveResult> SaveUpsertAsync(object model, CancellationToken ct = default) => Task.FromResult(new AdminSaveResult(true));
    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
    public Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default) => Task.FromResult(Enumerable.Empty<object>());
    public bool HasSecondaryApiList => false;
    public Task<IEnumerable<object>> GetSecondaryApiListAsync(string key, CancellationToken ct = default) => Task.FromResult(Enumerable.Empty<object>());
    public IAdminRegistryHandler? RegistryHandler => null;
    public IAdminCrudChildHandler? ChildHandler => null;
}

internal class MinimalChild : IAdminCrudChildHandler
{
    public string ChildType => "minimal";
    public string ChildDisplayName => "Minimal";
    public string[]? WriteRoles => null;
    public string ChildIndexViewPath => "i";
    public string ChildUpsertViewPath => "u";
    public Task<object?> GetChildIndexViewModelAsync(string parentKey, CancellationToken ct = default) => Task.FromResult<object?>(null);
    public Task<object?> GetChildUpsertViewModelAsync(string parentKey, Guid? id, CancellationToken ct = default) => Task.FromResult<object?>(null);
    public Task SetChildUpsertViewDataAsync(ViewDataDictionary viewData, string parentKey, CancellationToken ct = default) => Task.CompletedTask;
    public object CreateEmptyChildUpsertViewModel() => new object();
    public Task<AdminSaveResult> SaveChildUpsertAsync(string parentKey, object model, CancellationToken ct = default) => Task.FromResult(new AdminSaveResult(true));
    public Task<bool> DeleteChildAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
    public bool SupportsReorder => false;
    public Task<(bool Success, string? ErrorMessage)> ReorderAsync(string parentKey, List<Guid> orderedIds, CancellationToken ct = default)
        => Task.FromResult((false, (string?)null));
}

/// <summary>A MinimalHandler whose version delete succeeds, for the TryDeleteVersionAsync default's success path.</summary>
internal sealed class DeletingMinimalHandler : MinimalHandler, IAdminCrudHandler
{
    public Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
}

/// <summary>A MinimalChild with a restorable, deletable version, for the restore/delete defaults' success paths.</summary>
internal sealed class VersionedMinimalChild : MinimalChild, IAdminCrudChildHandler
{
    public object RestoreModel { get; } = new();
    public object? SavedModel { get; private set; }

    public Task<object?> GetChildRestoreVersionViewModelAsync(string parentKey, Guid historicalId, CancellationToken ct = default)
        => Task.FromResult<object?>(RestoreModel);

    public new Task<AdminSaveResult> SaveChildUpsertAsync(string parentKey, object model, CancellationToken ct = default)
    {
        SavedModel = model;
        return Task.FromResult(new AdminSaveResult(true));
    }

    public Task<bool> DeleteChildVersionAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
}

/// <summary>
/// Minimal IAdminRegistryHandler that does NOT override GetForm, so the default
/// interface implementation (returns NotFoundResult) is executed for coverage.
/// </summary>
internal sealed class MinimalRegistryHandler : IAdminRegistryHandler
{
    public IActionResult GetAll() => new JsonResult(new { });
    public IActionResult GetProperties(string name) => new JsonResult(new { });
}

/// <summary>
/// Minimal AdminCrudModel that does NOT override GetRestoreVersionViewModelAsync, so the base
/// virtual (returns null) is executed for coverage.
/// </summary>
internal sealed class MinimalCrudModel : AdminCrudModel<ContentBlockDTO>
{
    public MinimalCrudModel(IContentStore<ContentBlockDTO> store, IChangeSetScope changeSetScope,
        IRelationBinder relationBinder, IContentDeleteService deletes)
        : base(changeSetScope, relationBinder, deletes)
    {
        _store = store;
    }

    private readonly IContentStore<ContentBlockDTO> _store;

    protected override IContentStore<ContentBlockDTO> Store => _store;

    public bool SaveUpsertCoreCalled { get; private set; }

    protected override string VersionHistoryContentType => "minimal-crud";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/minimal-crud";
    protected override Task<List<ContentBlockDTO>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct) => Task.FromResult(new List<ContentBlockDTO>());
    public bool DeleteVersionResult { get; set; } = true;

    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct) => Task.FromResult(DeleteVersionResult);

    public override string ContentType => "minimal-crud";
    public override string DisplayName => "Minimal Crud";
    public override string IndexViewPath => "i";
    public override string UpsertViewPath => "u";

    public override Task<object> GetIndexViewModelAsync(CancellationToken ct = default) => Task.FromResult<object>("index");
    public override Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default) => Task.FromResult<object?>(null);
    public override object CreateEmptyUpsertViewModel() => new object();

    protected override Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        SaveUpsertCoreCalled = true;
        return Task.FromResult(new AdminSaveResult(true));
    }

    protected override Task<bool> DeleteSelfAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
    public override Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default) => Task.FromResult(Enumerable.Empty<object>());
}

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
    public async Task IAdminCrudChildHandler_DefaultMembers()
    {
        IAdminCrudChildHandler child = new MinimalChild();

        Assert.Multiple(async () =>
        {
            Assert.That(child.SupportsVersionHistory, Is.False);
            Assert.That(await child.GetChildVersionHistoryViewModelAsync("k", Guid.NewGuid()), Is.Null);
            Assert.That(await child.GetChildRestoreVersionViewModelAsync("k", Guid.NewGuid()), Is.Null);
            Assert.That(await child.DeleteChildVersionAsync(Guid.NewGuid()), Is.False);
            Assert.That(child.SupportsMove, Is.False);
            Assert.That(await child.MoveAsync(Guid.NewGuid(), "k", 0), Is.EqualTo((false, "Move is not supported for this child type.")));
        });
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
    public async Task IAdminCrudChildHandler_RestoreAndTryDeleteDefaults()
    {
        IAdminCrudChildHandler bare = new MinimalChild();
        var versioned = new VersionedMinimalChild();
        IAdminCrudChildHandler child = versioned;

        var missing = await bare.RestoreChildVersionAsync("p", Guid.NewGuid(), null);
        var restored = await child.RestoreChildVersionAsync("p", Guid.NewGuid(), 1);

        Assert.Multiple(async () =>
        {
            Assert.That(missing, Is.EqualTo(new AdminSaveResult(false, IAdminCrudChildHandler.VersionNotFoundMessage)));
            Assert.That(restored.Success, Is.True);
            Assert.That(versioned.SavedModel, Is.SameAs(versioned.RestoreModel));
            Assert.That(await bare.TryDeleteChildVersionAsync(Guid.NewGuid()), Is.EqualTo((false, IAdminCrudHandler.VersionNotDeletedMessage)));
            Assert.That(await child.TryDeleteChildVersionAsync(Guid.NewGuid()), Is.EqualTo((true, (string?)null)));
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
