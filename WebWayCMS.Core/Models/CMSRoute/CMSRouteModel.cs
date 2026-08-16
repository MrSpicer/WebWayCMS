using Microsoft.AspNetCore.Http;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Security;

namespace WebWayCMS.Models.CMSRoute;

public sealed class CMSRouteModel : ICMSRouteModel, IAdminCrudHandler
{
    private readonly ICMSRouteService _routeService;
    private readonly IMapper _mapper;

    public string ContentType => "cmsroutes";
    public string DisplayName => "CMS Route";
    public string[]? WriteRoles => null;
    public bool SupportsPublishing => false;
    public bool SupportsVersionHistory => false;
    public string IndexViewPath => "~/Views/AdminCMSRoute/CMSRoutes.cshtml";
    public string UpsertViewPath => "~/Views/AdminCMSRoute/CMSRouteUpsert.cshtml";

    public CMSRouteModel(ICMSRouteService routeService, IMapper mapper)
    {
        _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    public async Task<CMSRouteIndexViewModel> GetRouteIndexAsync(CancellationToken ct = default)
    {
        var routes = await _routeService.GetActiveRoutesAsync(ct);
        return new CMSRouteIndexViewModel
        {
            Routes = routes.Select(r => new CMSRouteItemViewModel
            {
                Id = r.Id,
                Pattern = r.Pattern,
                OwningContentType = r.OwningContentType,
                IsReserved = r.IsReserved
            }).ToList()
        };
    }

    public async Task<CMSRouteUpsertViewModel?> GetRouteUpsertAsync(Guid? id, CancellationToken ct = default)
    {
        if (id == null || id == Guid.Empty)
            return new CMSRouteUpsertViewModel();

        var dto = await _routeService.GetByIdAsync(id.Value, ct);
        if (dto == null) return null;

        return _mapper.Map<CMSRouteUpsertViewModel>(dto);
    }

    public async Task<(bool Success, string? ErrorMessage)> SaveRouteUpsertAsync(CMSRouteUpsertViewModel model, CancellationToken ct = default)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        var excludeRouteId = model.Id.HasValue && model.Id != Guid.Empty ? model.Id : null;
        var available = await _routeService.IsPatternAvailableAsync(model.Pattern, excludeRouteId: excludeRouteId, ct: ct);
        if (!available)
            return (false, "This route pattern is already in use.");

        var dto = _mapper.Map<CMSRouteDTO>(model);
        var result = await _routeService.UpsertAsync(dto, ct);
        return result.Success
            ? (true, null)
            : (false, result.ErrorMessage ?? "Failed to save route.");
    }

    public async Task<bool> DeleteRouteAsync(Guid id, CancellationToken ct = default)
    {
        return await _routeService.DeleteAsync(id, ct);
    }

    // IAdminCrudHandler members
    public async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
        => await GetRouteIndexAsync(ct);

    public async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
        => await GetRouteUpsertAsync(id, ct);

    public object CreateEmptyUpsertViewModel() => new CMSRouteUpsertViewModel();

    public async Task<AdminSaveResult> SaveUpsertAsync(object model, CancellationToken ct = default)
    {
        var vm = (CMSRouteUpsertViewModel)model;

        var validation = ModelValidator.Validate(vm);
        if (validation != null)
            return validation;

        var result = await SaveRouteUpsertAsync(vm, ct);
        return result.Success
            ? new AdminSaveResult(true)
            : new AdminSaveResult(false, result.ErrorMessage);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => await DeleteRouteAsync(id, ct);

    public async Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
    {
        var vm = await GetRouteIndexAsync(ct);
        return vm.Routes.Select(r => (object)new { id = r.Id, title = r.Pattern });
    }

    public bool HasSecondaryApiList => false;

    public Task<IEnumerable<object>> GetSecondaryApiListAsync(string key, CancellationToken ct = default)
        => Task.FromResult(Enumerable.Empty<object>());

    public IAdminRegistryHandler? RegistryHandler => null;
    public IAdminCrudChildHandler? ChildHandler => null;
}
