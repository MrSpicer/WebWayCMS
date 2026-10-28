using System.Text.Json;

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

    protected override string VersionHistoryContentType => "widgets";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/wadmin/widgets";
    protected override Task<List<WidgetRegistrationDTO>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct)
        => _store.GetAllVersionsAsync(nodeId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct)
        => _store.DeleteVersionAsync(id, ct);

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
    {
        if (id == null || id == Guid.Empty)
            return new WidgetRegistrationUpsertViewModel();

        var dto = await _store.GetCurrentDraftAsync(id.Value, ct);
        if (dto == null)
            return null;

        return new WidgetRegistrationUpsertViewModel
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
    }

    public override object CreateEmptyUpsertViewModel() => new WidgetRegistrationUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (WidgetRegistrationUpsertViewModel)model;
        var isEdit = vm.NodeId.HasValue && vm.NodeId != Guid.Empty;

        var (propertyDefinitionsJson, propError) = BuildPropertyDefinitions(vm.ConfigurationTypeName);
        if (propError != null)
            return new AdminSaveResult(false, propError);

        WidgetRegistrationDTO dto;
        if (isEdit)
        {
            var existing = await _store.GetCurrentDraftAsync(vm.NodeId!.Value, ct);
            if (existing == null)
                return new AdminSaveResult(false, "Widget registration not found.");

            dto = existing with
            {
                Version = existing.Version with
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
        }
        else
        {
            dto = new WidgetRegistrationDTO
            {
                Version = new ContentVersion
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
        }

        var result = await _store.SaveDraftAsync(dto, vm.ExpectedVersionNumber, ct);
        if (!result.Success)
            return new AdminSaveResult(false, result.ErrorMessage ?? "Save failed.");

        _widgetRegistry.Invalidate();
        return new AdminSaveResult(true, NodeId: result.NodeId);
    }

    protected override async Task<bool> DeleteSelfAsync(Guid id, CancellationToken ct = default)
    {
        var result = await _store.DeleteAsync(id, softDelete: false, ct);
        if (result)
            _widgetRegistry.Invalidate();
        return result;
    }

    public override async Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
    {
        var dtos = await _store.GetAllCurrentDraftsAsync(ct);
        return dtos
            .Select(d => (object)new
            {
                id = d.Version.Node.Id,
                title = d.DisplayName ?? d.ComponentName
            });
    }

    public override IAdminRegistryHandler? RegistryHandler => _registryHandler;

    public override async Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
    {
        var loaded = await LoadRestoreVersionAsync(historicalId, ct);
        if (loaded == null) return null;
        var historical = loaded.Value.Historical;
        return new WidgetRegistrationUpsertViewModel
        {
            NodeId = historical.Version.Node.Id,
            ExpectedVersionNumber = loaded.Value.CurrentVersionNumber,
            Title = historical.Version.Title,
            Slug = historical.Version.Slug,
            ComponentName = historical.ComponentName,
            DisplayName = historical.DisplayName,
            Description = historical.Description,
            Category = historical.Category,
            IconClass = historical.IconClass,
            Order = historical.Order,
            ConfigurationTypeName = historical.ConfigurationTypeName,
            IsActive = historical.IsActive,
            SupportsList = historical.SupportsList,
            IsPublished = historical.Version.State == ContentVersionState.Published,
        };
    }

    private static (string Json, string? Error) BuildPropertyDefinitions(string? configurationTypeName)
    {
        if (string.IsNullOrWhiteSpace(configurationTypeName))
            return ("[]", null);

        try
        {
            var type = Type.GetType(configurationTypeName, throwOnError: false);
            if (type == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = asm.GetType(configurationTypeName, throwOnError: false);
                    if (type != null)
                        break;
                }
            }

            if (type == null)
                return ("[]", $"Configuration type '{configurationTypeName}' could not be resolved.");

            var properties = FormPropertyBuilder.BuildPropertyInfos(type);
            var json = JsonSerializer.Serialize(properties);
            return (json, null);
        }
        catch (Exception ex)
        {
            return ("[]", $"Failed to build properties for type '{configurationTypeName}': {ex.Message}");
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static Type? ResolveConfigurationType(string? configurationTypeName)
    {
        if (string.IsNullOrWhiteSpace(configurationTypeName))
            return null;

        try
        {
            var type = Type.GetType(configurationTypeName, throwOnError: false);
            if (type != null)
                return type;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(configurationTypeName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
