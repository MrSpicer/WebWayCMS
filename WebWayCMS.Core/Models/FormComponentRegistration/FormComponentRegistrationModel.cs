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
    private readonly IContentStore<FormComponentRegistrationDTO> _store;
    private readonly IFormComponentRegistry _formComponentRegistry;
    private readonly FormComponentRegistrationRegistryHandler _registryHandler;

    protected override IContentStore<FormComponentRegistrationDTO> Store => _store;

    protected override string VersionHistoryContentType => "formcomponents";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/wadmin/formcomponents";
    protected override Task<List<FormComponentRegistrationDTO>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct)
        => _store.GetAllVersionsAsync(nodeId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct)
        => _store.DeleteVersionAsync(id, ct);

    public override string ContentType => "formcomponents";
    public override string DisplayName => "Form Component Registration";
    public override string IndexViewPath => "~/Views/FormComponentRegistration/Index.cshtml";
    public override string UpsertViewPath => "~/Views/FormComponentRegistration/FormComponentRegistrationUpsert.cshtml";

    public FormComponentRegistrationModel(
        IContentStore<FormComponentRegistrationDTO> store,
        IFormComponentRegistry formComponentRegistry,
        IChangeSetScope changeSetScope)
        : base(changeSetScope)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _formComponentRegistry = formComponentRegistry ?? throw new ArgumentNullException(nameof(formComponentRegistry));
        _registryHandler = new FormComponentRegistrationRegistryHandler(formComponentRegistry);
    }

    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
    {
        var dtos = await _store.GetAllCurrentDraftsAsync(ct);
        return new FormComponentRegistrationIndexViewModel { Registrations = dtos };
    }

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
    {
        if (id == null || id == Guid.Empty)
            return new FormComponentRegistrationUpsertViewModel();

        var dto = await _store.GetCurrentDraftAsync(id.Value, ct);
        if (dto == null)
            return null;

        return new FormComponentRegistrationUpsertViewModel
        {
            NodeId = dto.Version.Node.Id,
            ExpectedVersionNumber = dto.Version.VersionNumber,
            Title = dto.Version.Title,
            Slug = dto.Version.Slug,
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
            IsPublished = dto.Version.State == ContentVersionState.Published,
        };
    }

    public override object CreateEmptyUpsertViewModel() => new FormComponentRegistrationUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (FormComponentRegistrationUpsertViewModel)model;
        var isEdit = vm.NodeId.HasValue && vm.NodeId != Guid.Empty;

        FormComponentRegistrationDTO dto;
        if (isEdit)
        {
            var existing = await _store.GetCurrentDraftAsync(vm.NodeId!.Value, ct);
            if (existing == null)
                return new AdminSaveResult(false, "Form component registration not found.");

            dto = existing with
            {
                Version = existing.Version with
                {
                    Title = vm.Title,
                    Slug = vm.Slug ?? string.Empty,
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
        }
        else
        {
            dto = new FormComponentRegistrationDTO
            {
                Version = new ContentVersion
                {
                    Title = vm.Title,
                    Slug = vm.Slug ?? string.Empty,
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
        }

        var result = await _store.SaveDraftAsync(dto, vm.ExpectedVersionNumber, ct);
        if (!result.Success)
            return new AdminSaveResult(false, result.ErrorMessage ?? "Save failed.");

        _formComponentRegistry.Invalidate();
        return new AdminSaveResult(true, NodeId: result.NodeId);
    }

    public override async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var result = await _store.DeleteAsync(id, softDelete: false, ct);
        if (result)
            _formComponentRegistry.Invalidate();
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
        var historical = await _store.GetVersionAsync(historicalId, ct);
        if (historical == null) return null;
        return new FormComponentRegistrationUpsertViewModel
        {
            NodeId = historical.Version.Node.Id,
            ExpectedVersionNumber = historical.Version.VersionNumber,
            Title = historical.Version.Title,
            Slug = historical.Version.Slug,
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
            IsPublished = historical.Version.State == ContentVersionState.Published,
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
