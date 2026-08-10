using Microsoft.AspNetCore.Http;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Models.CMSRoute;

public sealed class CMSRouteModel : AdminCrudModel<CMSRouteDTO>, ICMSRouteModel
{
    private readonly ICMSRouteService _routeService;
    private readonly IMapper _mapper;

    protected override string VersionHistoryContentType => "cmsroutes";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/admin/cmsroutes";
    protected override Task<List<CMSRouteDTO>> GetAllVersionsAsync(Guid masterId, CancellationToken ct)
        => Task.FromResult(new List<CMSRouteDTO>());
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct)
        => Task.FromResult(false);

    public override string ContentType => "cmsroutes";
    public override string DisplayName => "CMS Route";
    public override string IndexViewPath => "~/Views/AdminCMSRoute/CMSRoutes.cshtml";
    public override string UpsertViewPath => "~/Views/AdminCMSRoute/CMSRouteUpsert.cshtml";
    public override bool SupportsVersionHistory => false;

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
                Id = r.ContentMeta.Id,
                MasterId = r.ContentMeta.MasterId,
                Version = r.ContentMeta.Version,
                Pattern = r.Pattern,
                OwningContentType = r.OwningContentType,
                IsReserved = r.IsReserved,
                IsPublished = r.ContentMeta.IsPublished,
                CreationDate = r.ContentMeta.CreationDate,
                ModificationDate = r.ContentMeta.ModificationDate
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

        var excludeMasterId = model.MasterId.HasValue && model.MasterId != Guid.Empty ? model.MasterId : null;
        var available = await _routeService.IsPatternAvailableAsync(model.Pattern, excludeMasterId, ct);
        if (!available)
            return (false, "This route pattern is already in use.");

        var dto = _mapper.Map<CMSRouteDTO>(model);
        await _routeService.UpsertAsync(dto, ct);
        return (true, null);
    }

    public async Task<bool> DeleteRouteAsync(Guid id, CancellationToken ct = default)
    {
        return await _routeService.DeleteAsync(id, ct);
    }

    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
        => await GetRouteIndexAsync(ct);

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
        => await GetRouteUpsertAsync(id, ct);

    public override object CreateEmptyUpsertViewModel() => new CMSRouteUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (CMSRouteUpsertViewModel)model;
        var result = await SaveRouteUpsertAsync(vm, ct);
        return result.Success
            ? new AdminSaveResult(true)
            : new AdminSaveResult(false, result.ErrorMessage);
    }

    public override async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => await DeleteRouteAsync(id, ct);

    public override async Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
    {
        var vm = await GetRouteIndexAsync(ct);
        return vm.Routes.Select(r => (object)new { id = r.MasterId, title = r.Pattern });
    }
}
