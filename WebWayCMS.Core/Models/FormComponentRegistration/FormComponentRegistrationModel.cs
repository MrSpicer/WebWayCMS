using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Models.FormComponentRegistration;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class FormComponentRegistrationModel : AdminCrudModel<FormComponentRegistrationDTO>
{
    private readonly IContentService<FormComponentRegistrationDTO> _service;
    private readonly IFormComponentRegistry _formComponentRegistry;
    private readonly FormComponentRegistrationRegistryHandler _registryHandler;

    protected override string VersionHistoryContentType => "formcomponents";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/wadmin/formcomponents";
    protected override Task<List<FormComponentRegistrationDTO>> GetAllVersionsAsync(Guid masterId, CancellationToken ct)
        => _service.GetAllVersionsAsync(masterId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct)
        => _service.DeleteAsync(id, softDelete: false, deleteHistory: false, ct: ct);

    public override string ContentType => "formcomponents";
    public override string DisplayName => "Form Component Registration";
    public override string IndexViewPath => "~/Views/FormComponentRegistration/Index.cshtml";
    public override string UpsertViewPath => "~/Views/FormComponentRegistration/FormComponentRegistrationUpsert.cshtml";

    public FormComponentRegistrationModel(
        IContentService<FormComponentRegistrationDTO> service,
        IFormComponentRegistry formComponentRegistry)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _formComponentRegistry = formComponentRegistry ?? throw new ArgumentNullException(nameof(formComponentRegistry));
        _registryHandler = new FormComponentRegistrationRegistryHandler(formComponentRegistry);
    }

    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
    {
        var dtos = await _service.GetAllAsync(ct);
        return new FormComponentRegistrationIndexViewModel { Registrations = dtos };
    }

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
    {
        if (id == null || id == Guid.Empty)
            return new FormComponentRegistrationUpsertViewModel();

        var dto = await _service.GetByIdAsync(id.Value, ct);
        if (dto == null)
            return null;

        return new FormComponentRegistrationUpsertViewModel
        {
            Id = dto.ContentId,
            MasterId = dto.ContentMeta.MasterId,
            Version = dto.ContentMeta.Version,
            Title = dto.ContentMeta.Title,
            Slug = dto.ContentMeta.Slug,
            IsPublished = dto.ContentMeta.IsPublished,
            ComponentName = dto.ComponentName,
            ViewComponentName = dto.ViewComponentName,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            Category = dto.Category,
            IconClass = dto.IconClass,
            Order = dto.Order,
            DataTypeNamesJson = dto.DataTypeNamesJson,
            EditorTypeAlias = dto.EditorTypeAlias,
            IsDefaultForType = dto.IsDefaultForType,
            WriteViewName = dto.WriteViewName,
            ReadViewName = dto.ReadViewName,
            IsActive = dto.IsActive,
        };
    }

    public override object CreateEmptyUpsertViewModel() => new FormComponentRegistrationUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (FormComponentRegistrationUpsertViewModel)model;
        var isEdit = vm.Id.HasValue && vm.Id != Guid.Empty;

        if (isEdit)
        {
            var existing = await _service.GetByIdAsync(vm.Id!.Value, ct);
            if (existing == null)
                return new AdminSaveResult(false, "Form component registration not found.");

            var updated = existing with
            {
                ContentMeta = existing.ContentMeta with
                {
                    Title = vm.Title,
                    Slug = vm.Slug ?? string.Empty,
                    IsPublished = vm.IsPublished,
                },
                ComponentName = vm.ComponentName,
                ViewComponentName = vm.ViewComponentName,
                DisplayName = vm.DisplayName,
                Description = vm.Description ?? string.Empty,
                Category = vm.Category,
                IconClass = vm.IconClass ?? string.Empty,
                Order = vm.Order,
                DataTypeNamesJson = vm.DataTypeNamesJson,
                EditorTypeAlias = vm.EditorTypeAlias,
                IsDefaultForType = vm.IsDefaultForType,
                WriteViewName = vm.WriteViewName,
                ReadViewName = vm.ReadViewName,
                IsActive = vm.IsActive,
            };

            var ok = await _service.UpdateAsync(updated, ct);
            if (ok)
                _formComponentRegistry.Invalidate();
            return ok ? new AdminSaveResult(true) : new AdminSaveResult(false, "Update failed.");
        }
        else
        {
            var dto = new FormComponentRegistrationDTO
            {
                ContentMeta = new ContentDTO
                {
                    Title = vm.Title,
                    Slug = vm.Slug ?? string.Empty,
                    IsPublished = vm.IsPublished,
                },
                ComponentName = vm.ComponentName,
                ViewComponentName = vm.ViewComponentName,
                DisplayName = vm.DisplayName,
                Description = vm.Description ?? string.Empty,
                Category = vm.Category,
                IconClass = vm.IconClass ?? string.Empty,
                Order = vm.Order,
                DataTypeNamesJson = vm.DataTypeNamesJson,
                EditorTypeAlias = vm.EditorTypeAlias,
                IsDefaultForType = vm.IsDefaultForType,
                WriteViewName = vm.WriteViewName,
                ReadViewName = vm.ReadViewName,
                IsActive = vm.IsActive,
            };

            await _service.CreateAsync(dto, ct);
            _formComponentRegistry.Invalidate();
            return new AdminSaveResult(true);
        }
    }

    public override async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var result = await _service.DeleteAsync(id, false, true, ct);
        if (result)
            _formComponentRegistry.Invalidate();
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
                title = d.DisplayName ?? d.ComponentName
            });
    }

    public override IAdminRegistryHandler? RegistryHandler => _registryHandler;

    public override async Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
    {
        var historical = await _service.GetByIdAsync(historicalId, ct);
        if (historical == null) return null;
        var latest = await _service.GetByMasterIdAsync(historical.ContentMeta.MasterId, ct);
        if (latest == null) return null;
        return new FormComponentRegistrationUpsertViewModel
        {
            Id = latest.ContentId,
            MasterId = latest.ContentMeta.MasterId,
            Version = latest.ContentMeta.Version,
            Title = historical.ContentMeta.Title,
            Slug = historical.ContentMeta.Slug,
            IsPublished = historical.ContentMeta.IsPublished,
            ComponentName = historical.ComponentName,
            ViewComponentName = historical.ViewComponentName,
            DisplayName = historical.DisplayName,
            Description = historical.Description,
            Category = historical.Category,
            IconClass = historical.IconClass,
            Order = historical.Order,
            DataTypeNamesJson = historical.DataTypeNamesJson,
            EditorTypeAlias = historical.EditorTypeAlias,
            IsDefaultForType = historical.IsDefaultForType,
            WriteViewName = historical.WriteViewName,
            ReadViewName = historical.ReadViewName,
            IsActive = historical.IsActive,
        };
    }

    private sealed class FormComponentRegistrationRegistryHandler : IAdminRegistryHandler
    {
        private readonly IFormComponentRegistry _registry;

        public FormComponentRegistrationRegistryHandler(IFormComponentRegistry registry)
        {
            _registry = registry;
        }

        public IActionResult GetAll()
        {
            var components = _registry.GetAll().Select(c => new
            {
                name = c.Name,
                displayName = c.DisplayName,
                description = c.Description,
                category = c.Category
            }).ToList();

            return new JsonResult(components);
        }

        public IActionResult GetProperties(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return new BadRequestObjectResult(new { error = "Component name is required." });

            var component = _registry.GetByName(name);
            if (component == null)
                return new NotFoundObjectResult(new { error = $"Component '{name}' not found." });

            return new JsonResult(new
            {
                componentName = component.Name,
                displayName = component.DisplayName,
                category = component.Category,
                properties = new List<object>()
            });
        }
    }
}
