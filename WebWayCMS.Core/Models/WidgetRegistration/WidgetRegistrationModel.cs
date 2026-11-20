using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Models.Shared;
using WebWayCMS.Services;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Models.WidgetRegistration;

public sealed class WidgetRegistrationModel : AdminCrudModel<WidgetRegistrationDTO>
{
    private readonly IContentStore<WidgetRegistrationDTO> _store;
    private readonly IWidgetRegistry _widgetRegistry;
    private readonly ContentZoneRegistryHandler _registryHandler;

    protected override IContentStore<WidgetRegistrationDTO> Store => _store;


    public override string ContentType => "widgets";
    public override string DisplayName => "Widget Registration";
    public override string IndexViewPath => "~/Views/WidgetRegistration/Index.cshtml";
    public override string UpsertViewPath => "~/Views/WidgetRegistration/WidgetRegistrationUpsert.cshtml";

    public WidgetRegistrationModel(
        IContentStore<WidgetRegistrationDTO> store,
        IWidgetRegistry widgetRegistry,
        IViewDiscoveryService viewDiscoveryService,
        IChangeSetScope changeSetScope,
        IRelationBinder relationBinder,
        IContentDeleteService deletes)
        : base(changeSetScope, relationBinder, deletes)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _widgetRegistry = widgetRegistry ?? throw new ArgumentNullException(nameof(widgetRegistry));
        // The widgets admin exposes the same component registry endpoints as the zone editor.
        _registryHandler = new ContentZoneRegistryHandler(
            widgetRegistry,
            viewDiscoveryService ?? throw new ArgumentNullException(nameof(viewDiscoveryService)));
    }

    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
    {
        var dtos = await _store.GetAllCurrentDraftsAsync(ct);
        return new WidgetRegistrationIndexViewModel { Registrations = dtos };
    }

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
        => await LoadUpsertAsync(id, ToUpsertViewModel, ct);

    /// <summary>The edit form for <paramref name="dto"/>, expecting its own version (a restore replaces the expectation).</summary>
    private static WidgetRegistrationUpsertViewModel ToUpsertViewModel(WidgetRegistrationDTO dto) => new()
    {
        NodeId = dto.Version.Node.Id,
        ExpectedVersionNumber = dto.Version.VersionNumber,
        Title = dto.Version.Title,
        Slug = dto.Version.Slug,
        ComponentName = dto.ComponentName,
        DisplayName = dto.DisplayName,
        Description = dto.Description,
        Category = dto.Category,
        IconClass = dto.IconClass,
        Order = dto.Order,
        ConfigurationTypeName = dto.ConfigurationTypeName,
        IsActive = dto.IsActive,
        SupportsList = dto.SupportsList,
        IsPublished = dto.Version.State == ContentVersionState.Published,
    };

    public override object CreateEmptyUpsertViewModel() => new WidgetRegistrationUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (WidgetRegistrationUpsertViewModel)model;
        var isEdit = vm.NodeId.HasValue && vm.NodeId != Guid.Empty;

        var (propertyDefinitionsJson, propError) = PropertyDefinitionsJson.TryBuild(vm.ConfigurationTypeName);
        if (propError != null)
            return new AdminSaveResult(false, propError);

        // An edit starts from the current draft (keeping what the form doesn't carry); a create from an empty item.
        var baseline = new WidgetRegistrationDTO { Version = new ContentVersion() };
        if (isEdit)
        {
            var existing = await _store.GetCurrentDraftAsync(vm.NodeId!.Value, ct);
            if (existing == null)
                return new AdminSaveResult(false, "Widget registration not found.");
            baseline = existing;
        }

        var dto = baseline with
        {
            Version = baseline.Version with
            {
                Title = vm.Title,
                Slug = vm.Slug ?? string.Empty,
            },
            ComponentName = vm.ComponentName,
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
            _widgetRegistry.Invalidate();
        return result;
    }

    protected override async Task<bool> DeleteSelfAsync(Guid id, CancellationToken ct = default)
    {
        var result = await base.DeleteSelfAsync(id, ct);
        if (result)
            _widgetRegistry.Invalidate();
        return result;
    }

    protected override string PickerTitle(WidgetRegistrationDTO item) => item.DisplayName ?? item.ComponentName;

    public override IAdminRegistryHandler? RegistryHandler => _registryHandler;

    public override Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
        => MapRestoreAsync(historicalId, ToUpsertViewModel, ct);
}
