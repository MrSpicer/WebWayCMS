using Microsoft.AspNetCore.Http;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Shared;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Models.Route;

/// <summary>
/// The versioned <c>routes</c> content type. Route items are owned by their content type's
/// <c>routes</c> relation and published with it; published versions are projected into
/// <see cref="CMSRouteDTO"/>. Not shown in the navbar: it is edited through the reference modal.
/// </summary>
public sealed class RouteModel : AdminCrudModel<RouteDTO>
{
    public const string ContentTypeKey = "routes";

    private readonly IContentStore<RouteDTO> _store;
    private readonly ICMSRouteService _routeService;
    private readonly IMapper _mapper;

    protected override IContentStore<RouteDTO> Store => _store;
    // Routes have no index of their own in the navbar: their history goes back to the projected CMS routes.
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/wadmin/cmsroutes";

    public override string ContentType => ContentTypeKey;
    public override string DisplayName => "Route";
    public override string[]? WriteRoles => UserProfileRules.AdminOrEditor;
    public override bool SupportsPublishing => false;
    public override string IndexViewPath => "~/Views/AdminRoute/Routes.cshtml";
    public override string UpsertViewPath => "~/Views/AdminRoute/RouteUpsert.cshtml";

    public RouteModel(
        IContentStore<RouteDTO> store,
        ICMSRouteService routeService,
        IMapper mapper,
        IChangeSetScope changeSetScope,
        IRelationBinder relationBinder,
        IContentDeleteService deletes)
        : base(changeSetScope, relationBinder, deletes)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
        => await _store.GetAllCurrentDraftsAsync(ct);

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
        => await LoadUpsertAsync(id, _mapper.Map<RouteUpsertViewModel>, ct);

    public override object CreateEmptyUpsertViewModel() => new RouteUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (RouteUpsertViewModel)model;

        if (vm.Mode == RouteMode.Explicit)
        {
            if (RoutePatternRules.Validate(vm.Pattern) is { } error)
                return new AdminSaveResult(false, error, nameof(RouteUpsertViewModel.Pattern));

            var pattern = RoutePatternRules.Normalize(vm.Pattern);
            // A published route's own projected row holds its pattern; only another row is a collision.
            var ownRowId = vm.NodeId is { } routeNodeId
                ? (await _routeService.GetAllRoutesAsync(ct) ?? []).FirstOrDefault(r => r.RouteNodeId == routeNodeId)?.Id
                : null;
            if (!await _routeService.IsPatternAvailableAsync(pattern, null, ownRowId, ct))
                return new AdminSaveResult(false, "This route pattern is already in use by another content item.", nameof(RouteUpsertViewModel.Pattern));

            vm.Pattern = pattern;
        }

        // RouteUpsertViewModel is not a BaseContentViewModel, so it saves without the SaveDraftAsync helper.
        var result = await Store.SaveDraftAsync(_mapper.Map<RouteDTO>(vm), vm.ExpectedVersionNumber, ct);
        if (!result.Success) return new AdminSaveResult(false, result.ErrorMessage ?? "Unable to save the route.");
        vm.NodeId = result.NodeId;
        return new AdminSaveResult(true, NodeId: vm.NodeId);
    }

    protected override async Task<bool> DeleteSelfAsync(Guid id, CancellationToken ct = default)
    {
        await _routeService.DeleteByRouteNodeAsync(id, ct);
        return await base.DeleteSelfAsync(id, ct);
    }

    public override async Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
    {
        var routes = await _store.GetAllCurrentDraftsAsync(ct);
        return routes.Select(r => (object)new
        {
            id = r.Version.Node.Id,
            title = string.IsNullOrWhiteSpace(r.Pattern) ? "(derived)" : r.Pattern
        });
    }
}
