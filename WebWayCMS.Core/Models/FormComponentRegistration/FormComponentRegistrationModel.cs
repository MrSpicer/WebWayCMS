using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;
using WebWayCMS.Security;

namespace WebWayCMS.Models.FormComponentRegistration;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class FormComponentRegistrationModel : IAdminCrudHandler
{
    private readonly IFormComponentRegistrationService _service;
    private readonly IFormComponentRegistry _formComponentRegistry;
    private readonly FormComponentRegistrationRegistryHandler _registryHandler;

    public string ContentType => "formcomponents";
    public string DisplayName => "Form Component Registration";
    public string[]? WriteRoles => null;
    public bool SupportsPublishing => false;
    public bool SupportsVersionHistory => false;
    public string IndexViewPath => "~/Views/FormComponentRegistration/Index.cshtml";
    public string UpsertViewPath => "~/Views/FormComponentRegistration/FormComponentRegistrationUpsert.cshtml";

    public FormComponentRegistrationModel(
        IFormComponentRegistrationService service,
        IFormComponentRegistry formComponentRegistry)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _formComponentRegistry = formComponentRegistry ?? throw new ArgumentNullException(nameof(formComponentRegistry));
        _registryHandler = new FormComponentRegistrationRegistryHandler(formComponentRegistry);
    }

    public async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
    {
        var dtos = await _service.GetAllAsync(ct);
        return new FormComponentRegistrationIndexViewModel { Registrations = dtos };
    }

    public async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
    {
        if (id == null || id == Guid.Empty)
            return new FormComponentRegistrationUpsertViewModel();

        var dto = await _service.GetByIdAsync(id.Value, ct);
        if (dto == null)
            return null;

        return ToViewModel(dto);
    }

    public object CreateEmptyUpsertViewModel() => new FormComponentRegistrationUpsertViewModel();

    public async Task<AdminSaveResult> SaveUpsertAsync(object model, CancellationToken ct = default)
    {
        var vm = (FormComponentRegistrationUpsertViewModel)model;

        var validation = ModelValidator.Validate(vm);
        if (validation != null)
            return validation;

        var dto = ToDto(vm);
        var result = await _service.UpsertAsync(dto, ct);

        if (!result.Success)
            return new AdminSaveResult(false, result.ErrorMessage);

        _formComponentRegistry.Invalidate();
        return new AdminSaveResult(true);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var result = await _service.DeleteAsync(id, ct);
        if (result)
            _formComponentRegistry.Invalidate();
        return result;
    }

    public async Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
    {
        var dtos = await _service.GetAllAsync(ct);
        return dtos
            .Select(d => (object)new
            {
                id = d.Id,
                title = d.DisplayName ?? d.ComponentName
            });
    }

    public bool HasSecondaryApiList => false;

    public Task<IEnumerable<object>> GetSecondaryApiListAsync(string key, CancellationToken ct = default)
        => Task.FromResult(Enumerable.Empty<object>());

    public IAdminRegistryHandler? RegistryHandler => _registryHandler;

    public IAdminCrudChildHandler? ChildHandler => null;

    private static FormComponentRegistrationUpsertViewModel ToViewModel(FormComponentRegistrationDTO dto)
        => new()
        {
            Id = dto.Id,
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

    private static FormComponentRegistrationDTO ToDto(FormComponentRegistrationUpsertViewModel vm)
        => new()
        {
            Id = vm.Id ?? Guid.Empty,
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
