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
    private readonly IContentService<PageControllerRegistrationDTO> _service;
    private readonly IPageControllerRegistry _registry;

    protected override string VersionHistoryContentType => "pagetypes";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/admin/pagetypes";
    protected override Task<List<PageControllerRegistrationDTO>> GetAllVersionsAsync(Guid masterId, CancellationToken ct)
        => _service.GetAllVersionsAsync(masterId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct)
        => _service.DeleteAsync(id, softDelete: false, deleteHistory: false, ct: ct);

    public override string ContentType => "pagetypes";
    public override string DisplayName => "Page Controller Registration";
    public override string IndexViewPath => "~/Views/PageControllerRegistration/Index.cshtml";
    public override string UpsertViewPath => "~/Views/PageControllerRegistration/PageControllerRegistrationUpsert.cshtml";

    public PageControllerRegistrationModel(
        IContentService<PageControllerRegistrationDTO> service,
        IPageControllerRegistry registry)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
    {
        var dtos = await _service.GetAllAsync(ct);
        return new PageControllerRegistrationIndexViewModel { Registrations = dtos };
    }

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
    {
        if (id == null || id == Guid.Empty)
            return new PageControllerRegistrationUpsertViewModel();

        var dto = await _service.GetByIdAsync(id.Value, ct);
        if (dto == null)
            return null;

        return new PageControllerRegistrationUpsertViewModel
        {
            Id = dto.ContentId,
            MasterId = dto.ContentMeta.MasterId,
            Version = dto.ContentMeta.Version,
            Title = dto.ContentMeta.Title,
            Slug = dto.ContentMeta.Slug,
            IsPublished = dto.ContentMeta.IsPublished,
            ControllerName = dto.ControllerName,
            ControllerTypeName = dto.ControllerTypeName,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            Category = dto.Category,
            IconClass = dto.IconClass,
            Order = dto.Order,
            ConfigurationTypeName = dto.ConfigurationTypeName,
            IsActive = dto.IsActive,
        };
    }

    public override object CreateEmptyUpsertViewModel() => new PageControllerRegistrationUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (PageControllerRegistrationUpsertViewModel)model;
        var isEdit = vm.Id.HasValue && vm.Id != Guid.Empty;

        if (isEdit)
        {
            var existing = await _service.GetByIdAsync(vm.Id!.Value, ct);
            if (existing == null)
                return new AdminSaveResult(false, "Page controller registration not found.");

            var (propertyDefinitionsJson, propError) = BuildPropertyDefinitions(vm.ConfigurationTypeName);
            if (propError != null)
                return new AdminSaveResult(false, propError);

            var updated = existing with
            {
                ContentMeta = existing.ContentMeta with
                {
                    Title = vm.Title,
                    Slug = vm.Slug ?? string.Empty,
                    IsPublished = vm.IsPublished,
                },
                ControllerName = vm.ControllerName,
                ControllerTypeName = vm.ControllerTypeName,
                DisplayName = vm.DisplayName,
                Description = vm.Description,
                Category = vm.Category,
                IconClass = vm.IconClass,
                Order = vm.Order,
                ConfigurationTypeName = vm.ConfigurationTypeName,
                PropertyDefinitionsJson = propertyDefinitionsJson,
                IsActive = vm.IsActive,
            };

            var ok = await _service.UpdateAsync(updated, ct);
            if (ok)
                _registry.Invalidate();
            return ok ? new AdminSaveResult(true) : new AdminSaveResult(false, "Update failed.");
        }
        else
        {
            var (propertyDefinitionsJson, propError) = BuildPropertyDefinitions(vm.ConfigurationTypeName);
            if (propError != null)
                return new AdminSaveResult(false, propError);

            var dto = new PageControllerRegistrationDTO
            {
                ContentMeta = new ContentDTO
                {
                    Title = vm.Title,
                    Slug = vm.Slug ?? string.Empty,
                    IsPublished = vm.IsPublished,
                },
                ControllerName = vm.ControllerName,
                ControllerTypeName = vm.ControllerTypeName,
                DisplayName = vm.DisplayName,
                Description = vm.Description,
                Category = vm.Category,
                IconClass = vm.IconClass,
                Order = vm.Order,
                ConfigurationTypeName = vm.ConfigurationTypeName,
                PropertyDefinitionsJson = propertyDefinitionsJson,
                IsActive = vm.IsActive,
            };

            await _service.CreateAsync(dto, ct);
            _registry.Invalidate();
            return new AdminSaveResult(true);
        }
    }

    public override async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var result = await _service.DeleteAsync(id, false, true, ct);
        if (result)
            _registry.Invalidate();
        return result;
    }

    public override async Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
    {
        var dtos = await _service.GetAllAsync(ct);
        return dtos
            .Where(d => d.ContentMeta.IsPublished)
            .Select(d => (object)new
            {
                id = d.ContentMeta.MasterId,
                title = d.DisplayName ?? d.ControllerName
            });
    }

    public override async Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
    {
        var historical = await _service.GetByIdAsync(historicalId, ct);
        if (historical == null) return null;
        var latest = await _service.GetByMasterIdAsync(historical.ContentMeta.MasterId, ct);
        if (latest == null) return null;
        return new PageControllerRegistrationUpsertViewModel
        {
            Id = latest.ContentId,
            MasterId = latest.ContentMeta.MasterId,
            Version = latest.ContentMeta.Version,
            Title = historical.ContentMeta.Title,
            Slug = historical.ContentMeta.Slug,
            IsPublished = historical.ContentMeta.IsPublished,
            ControllerName = historical.ControllerName,
            ControllerTypeName = historical.ControllerTypeName,
            DisplayName = historical.DisplayName,
            Description = historical.Description,
            Category = historical.Category,
            IconClass = historical.IconClass,
            Order = historical.Order,
            ConfigurationTypeName = historical.ConfigurationTypeName,
            IsActive = historical.IsActive,
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
