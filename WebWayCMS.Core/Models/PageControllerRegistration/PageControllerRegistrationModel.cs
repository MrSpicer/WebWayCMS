using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;
using WebWayCMS.Models.Shared;
using WebWayCMS.Pages;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Models.PageControllerRegistration;

public sealed class PageControllerRegistrationModel : AdminCrudModel<PageControllerRegistrationDTO>
{
    private readonly IContentStore<PageControllerRegistrationDTO> _store;
    private readonly IPageControllerRegistry _registry;

    protected override IContentStore<PageControllerRegistrationDTO> Store => _store;


    public override string ContentType => "pagetypes";
    public override string DisplayName => "Page Controller Registration";
    public override string IndexViewPath => "~/Views/PageControllerRegistration/Index.cshtml";
    public override string UpsertViewPath => "~/Views/PageControllerRegistration/PageControllerRegistrationUpsert.cshtml";

    public PageControllerRegistrationModel(
        IContentStore<PageControllerRegistrationDTO> store,
        IPageControllerRegistry registry,
        IChangeSetScope changeSetScope,
        IRelationBinder relationBinder,
        IContentDeleteService deletes)
        : base(changeSetScope, relationBinder, deletes)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
    {
        var dtos = await _store.GetAllCurrentDraftsAsync(ct);
        return new PageControllerRegistrationIndexViewModel { Registrations = dtos };
    }

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
        => await LoadUpsertAsync(id, ToUpsertViewModel, ct);

    /// <summary>The edit form for <paramref name="dto"/>, expecting its own version (a restore replaces the expectation).</summary>
    private static PageControllerRegistrationUpsertViewModel ToUpsertViewModel(PageControllerRegistrationDTO dto) => new()
    {
        NodeId = dto.Version.Node.Id,
        ExpectedVersionNumber = dto.Version.VersionNumber,
        Title = dto.Version.Title,
        Slug = dto.Version.Slug,
        ControllerName = dto.ControllerName,
        ControllerTypeName = dto.ControllerTypeName,
        DisplayName = dto.DisplayName,
        Description = dto.Description,
        Category = dto.Category,
        IconClass = dto.IconClass,
        Order = dto.Order,
        ConfigurationTypeName = dto.ConfigurationTypeName,
        IsActive = dto.IsActive,
        IsPublished = dto.Version.State == ContentVersionState.Published,
    };

    public override object CreateEmptyUpsertViewModel() => new PageControllerRegistrationUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (PageControllerRegistrationUpsertViewModel)model;
        var isEdit = vm.NodeId.HasValue && vm.NodeId != Guid.Empty;

        var (propertyDefinitionsJson, propError) = PropertyDefinitionsJson.TryBuild(vm.ConfigurationTypeName);
        if (propError != null)
            return new AdminSaveResult(false, propError);

        // An edit starts from the current draft (keeping what the form doesn't carry); a create from an empty item.
        var baseline = new PageControllerRegistrationDTO { Version = new ContentVersion() };
        if (isEdit)
        {
            var existing = await _store.GetCurrentDraftAsync(vm.NodeId!.Value, ct);
            if (existing == null)
                return new AdminSaveResult(false, "Page controller registration not found.");
            baseline = existing;
        }

        var dto = baseline with
        {
            Version = baseline.Version with
            {
                Title = vm.Title,
                Slug = vm.Slug ?? string.Empty,
            },
            ControllerName = vm.ControllerName,
            ControllerTypeName = vm.ControllerTypeName,
            DisplayName = vm.DisplayName,
            Description = vm.Description ?? string.Empty,
            Category = vm.Category,
            IconClass = vm.IconClass ?? string.Empty,
            Order = vm.Order,
            ConfigurationTypeName = vm.ConfigurationTypeName,
            PropertyDefinitionsJson = propertyDefinitionsJson,
            IsActive = vm.IsActive,
        };

        var result = await SaveDraftAsync(dto, vm, "Save failed.", ct);
        if (result.Success)
            _registry.Invalidate();
        return result;
    }

    protected override async Task<bool> DeleteSelfAsync(Guid id, CancellationToken ct = default)
    {
        var result = await base.DeleteSelfAsync(id, ct);
        if (result)
            _registry.Invalidate();
        return result;
    }

    protected override string PickerTitle(PageControllerRegistrationDTO item) => item.DisplayName ?? item.ControllerName;

    public override Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
        => MapRestoreAsync(historicalId, ToUpsertViewModel, ct);
}
