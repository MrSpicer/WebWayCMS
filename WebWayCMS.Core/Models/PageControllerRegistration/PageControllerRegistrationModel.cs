using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;
using WebWayCMS.Models.Shared;
using WebWayCMS.Pages;

namespace WebWayCMS.Models.PageControllerRegistration;

public sealed class PageControllerRegistrationModel : AdminCrudModel<PageControllerRegistrationDTO>
{
    private readonly IContentStore<PageControllerRegistrationDTO> _store;
    private readonly IPageControllerRegistry _registry;

    protected override IContentStore<PageControllerRegistrationDTO> Store => _store;

    protected override string VersionHistoryContentType => "pagetypes";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/wadmin/pagetypes";
    protected override Task<List<PageControllerRegistrationDTO>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct)
        => _store.GetAllVersionsAsync(nodeId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct)
        => _store.DeleteVersionAsync(id, ct);

    public override string ContentType => "pagetypes";
    public override string DisplayName => "Page Controller Registration";
    public override string IndexViewPath => "~/Views/PageControllerRegistration/Index.cshtml";
    public override string UpsertViewPath => "~/Views/PageControllerRegistration/PageControllerRegistrationUpsert.cshtml";

    public PageControllerRegistrationModel(
        IContentStore<PageControllerRegistrationDTO> store,
        IPageControllerRegistry registry,
        IChangeSetScope changeSetScope)
        : base(changeSetScope)
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
    {
        if (id == null || id == Guid.Empty)
            return new PageControllerRegistrationUpsertViewModel();

        var dto = await _store.GetCurrentDraftAsync(id.Value, ct);
        if (dto == null)
            return null;

        return new PageControllerRegistrationUpsertViewModel
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
    }

    public override object CreateEmptyUpsertViewModel() => new PageControllerRegistrationUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (PageControllerRegistrationUpsertViewModel)model;
        var isEdit = vm.NodeId.HasValue && vm.NodeId != Guid.Empty;

        var (propertyDefinitionsJson, propError) = BuildPropertyDefinitions(vm.ConfigurationTypeName);
        if (propError != null)
            return new AdminSaveResult(false, propError);

        PageControllerRegistrationDTO dto;
        if (isEdit)
        {
            var existing = await _store.GetCurrentDraftAsync(vm.NodeId!.Value, ct);
            if (existing == null)
                return new AdminSaveResult(false, "Page controller registration not found.");

            dto = existing with
            {
                Version = existing.Version with
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
        }
        else
        {
            dto = new PageControllerRegistrationDTO
            {
                Version = new ContentVersion
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
        }

        var result = await _store.SaveDraftAsync(dto, vm.ExpectedVersionNumber, ct);
        if (!result.Success)
            return new AdminSaveResult(false, result.ErrorMessage ?? "Save failed.");

        _registry.Invalidate();
        return new AdminSaveResult(true, NodeId: result.NodeId);
    }

    public override async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var result = await _store.DeleteAsync(id, softDelete: false, ct);
        if (result)
            _registry.Invalidate();
        return result;
    }

    public override async Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
    {
        var dtos = await _store.GetAllCurrentDraftsAsync(ct);
        return dtos
            .Select(d => (object)new
            {
                id = d.Version.Node.Id,
                title = d.DisplayName ?? d.ControllerName
            });
    }

    public override async Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
    {
        var loaded = await LoadRestoreVersionAsync(historicalId, ct);
        if (loaded == null) return null;
        var historical = loaded.Value.Historical;
        return new PageControllerRegistrationUpsertViewModel
        {
            NodeId = historical.Version.Node.Id,
            ExpectedVersionNumber = loaded.Value.CurrentVersionNumber,
            Title = historical.Version.Title,
            Slug = historical.Version.Slug,
            ControllerName = historical.ControllerName,
            ControllerTypeName = historical.ControllerTypeName,
            DisplayName = historical.DisplayName,
            Description = historical.Description,
            Category = historical.Category,
            IconClass = historical.IconClass,
            Order = historical.Order,
            ConfigurationTypeName = historical.ConfigurationTypeName,
            IsActive = historical.IsActive,
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
}
