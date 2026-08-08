using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;
using WebWayCMS.Models.Shared;
using WebWayCMS.Services;

namespace WebWayCMS.Models.WidgetRegistration;

public sealed class WidgetRegistrationModel : AdminCrudModel<WidgetRegistrationDTO>
{
    private readonly IContentService<WidgetRegistrationDTO> _service;
    private readonly IWidgetRegistry _widgetRegistry;
    private readonly IViewDiscoveryService _viewDiscoveryService;
    private readonly WidgetRegistrationRegistryHandler _registryHandler;

    protected override string VersionHistoryContentType => "widgets";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/admin/widgets";
    protected override Task<List<WidgetRegistrationDTO>> GetAllVersionsAsync(Guid masterId, CancellationToken ct)
        => _service.GetAllVersionsAsync(masterId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct)
        => _service.DeleteAsync(id, softDelete: false, deleteHistory: false, ct: ct);

    public override string ContentType => "widgets";
    public override string DisplayName => "Widget Registration";
    public override string IndexViewPath => "~/Views/WidgetRegistration/Index.cshtml";
    public override string UpsertViewPath => "~/Views/WidgetRegistration/WidgetRegistrationUpsert.cshtml";

    public WidgetRegistrationModel(
        IContentService<WidgetRegistrationDTO> service,
        IWidgetRegistry widgetRegistry,
        IViewDiscoveryService viewDiscoveryService)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _widgetRegistry = widgetRegistry ?? throw new ArgumentNullException(nameof(widgetRegistry));
        _viewDiscoveryService = viewDiscoveryService ?? throw new ArgumentNullException(nameof(viewDiscoveryService));
        _registryHandler = new WidgetRegistrationRegistryHandler(widgetRegistry, viewDiscoveryService);
    }

    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
    {
        var dtos = await _service.GetAllAsync(ct);
        return new WidgetRegistrationIndexViewModel { Registrations = dtos };
    }

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
    {
        if (id == null || id == Guid.Empty)
            return new WidgetRegistrationUpsertViewModel();

        var dto = await _service.GetByIdAsync(id.Value, ct);
        if (dto == null)
            return null;

        return new WidgetRegistrationUpsertViewModel
        {
            Id = dto.ContentId,
            MasterId = dto.ContentMeta.MasterId,
            Version = dto.ContentMeta.Version,
            Title = dto.ContentMeta.Title,
            Slug = dto.ContentMeta.Slug,
            IsPublished = dto.ContentMeta.IsPublished,
            ComponentName = dto.ComponentName,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            Category = dto.Category,
            IconClass = dto.IconClass,
            Order = dto.Order,
            ConfigurationTypeName = dto.ConfigurationTypeName,
            IsActive = dto.IsActive,
        };
    }

    public override object CreateEmptyUpsertViewModel() => new WidgetRegistrationUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (WidgetRegistrationUpsertViewModel)model;
        var isEdit = vm.Id.HasValue && vm.Id != Guid.Empty;

        if (isEdit)
        {
            var existing = await _service.GetByIdAsync(vm.Id!.Value, ct);
            if (existing == null)
                return new AdminSaveResult(false, "Widget registration not found.");

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
                ComponentName = vm.ComponentName,
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
                _widgetRegistry.Invalidate();
            return ok ? new AdminSaveResult(true) : new AdminSaveResult(false, "Update failed.");
        }
        else
        {
            var (propertyDefinitionsJson, propError) = BuildPropertyDefinitions(vm.ConfigurationTypeName);
            if (propError != null)
                return new AdminSaveResult(false, propError);

            var dto = new WidgetRegistrationDTO
            {
                ContentMeta = new ContentDTO
                {
                    Title = vm.Title,
                    Slug = vm.Slug ?? string.Empty,
                    IsPublished = vm.IsPublished,
                },
                ComponentName = vm.ComponentName,
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
            _widgetRegistry.Invalidate();
            return new AdminSaveResult(true);
        }
    }

    public override async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var result = await _service.DeleteAsync(id, false, true, ct);
        if (result)
            _widgetRegistry.Invalidate();
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
        return new WidgetRegistrationUpsertViewModel
        {
            Id = latest.ContentId,
            MasterId = latest.ContentMeta.MasterId,
            Version = latest.ContentMeta.Version,
            Title = historical.ContentMeta.Title,
            Slug = historical.ContentMeta.Slug,
            IsPublished = historical.ContentMeta.IsPublished,
            ComponentName = historical.ComponentName,
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

    private sealed class WidgetRegistrationRegistryHandler : IAdminRegistryHandler
    {
        private readonly IWidgetRegistry _registry;
        private readonly IViewDiscoveryService _viewDiscoveryService;
        private readonly Serilog.ILogger _logger = Serilog.Log.ForContext<WidgetRegistrationRegistryHandler>();

        public WidgetRegistrationRegistryHandler(
            IWidgetRegistry registry,
            IViewDiscoveryService viewDiscoveryService)
        {
            _registry = registry;
            _viewDiscoveryService = viewDiscoveryService;
        }

        public IActionResult GetAll()
        {
            var components = _registry.GetAllComponents().Select(c => new
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

            var properties = component.Properties.Select(p =>
            {
                Dictionary<string, string> dropdownOptions = p.DropdownOptions;

                if (p.EditorType == EditorType.ViewPicker && !string.IsNullOrWhiteSpace(p.ViewComponentName))
                {
                    var views = _viewDiscoveryService.GetAvailableViews(p.ViewComponentName);
                    if (views.Any())
                        dropdownOptions = views.ToDictionary(v => v, v => v);
                    else
                    {
                        _logger.Warning("No views found for ViewComponent '{ComponentName}'", p.ViewComponentName);
                        dropdownOptions = new Dictionary<string, string>();
                    }
                }

                return new
                {
                    name = p.Name,
                    label = p.Label,
                    helpText = p.HelpText,
                    placeholder = p.Placeholder,
                    editorType = p.EditorType.ToString().ToLowerInvariant(),
                    isRequired = p.IsRequired,
                    defaultValue = p.DefaultValue,
                    order = p.Order,
                    group = p.Group,
                    entityType = p.EntityType,
                    dropdownOptions,
                    viewComponentName = p.ViewComponentName,
                    min = p.Min,
                    max = p.Max,
                    maxLength = p.MaxLength
                };
            }).OrderBy(p => p.order).ToList();

            return new JsonResult(new
            {
                componentName = component.Name,
                displayName = component.DisplayName,
                category = component.Category,
                properties
            });
        }
    }
}
