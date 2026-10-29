using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Data.Slugs;
using WebWayCMS.Forms;
using WebWayCMS.Interfaces;
using WebWayCMS.Mapping;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Models.PageHistory;
using WebWayCMS.Models.Shared;
using WebWayCMS.Pages;
using WebWayCMS.Services;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Models.Page;

public sealed class PageModel : AdminCrudModel<PageDTO>, IPageModel, IRoutableContent
{
    private readonly IContentStore<PageDTO> _store;
    private readonly IMapper _mapper;
    private readonly PageRegistryHandler _registryHandler;
    private readonly IRouteRegistrationService _routeRegistration;
    private readonly ICMSRouteService _routeService;
    private readonly IPageControllerRegistry _controllerRegistry;
    private readonly IContentZoneService _contentZoneService;
    private readonly IPageHistoryService _pageHistory;
    private readonly IContentStore<RouteDTO> _routeStore;
    private readonly ICmsTransaction _transaction;
    private readonly ICMSRouteRegistry _routeRegistry;

    protected override IContentStore<PageDTO> Store => _store;

    protected override string VersionHistoryContentType => "pages";
    protected override string GetVersionHistoryBackUrl(string? parentKey = null) => "/wadmin/pages";
    protected override Task<List<PageDTO>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct) => _store.GetAllVersionsAsync(nodeId, ct);
    protected override Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct) => _store.DeleteVersionAsync(id, ct);

    public override string ContentType => "pages";
    public override string DisplayName => "Page";
    public override string IndexViewPath => "~/Views/AdminPage/Pages.cshtml";
    public override string UpsertViewPath => "~/Views/AdminPage/PageUpsert.cshtml";
    public override IAdminRegistryHandler? RegistryHandler => _registryHandler;
    public override bool SupportsPreview => true;

    string IRoutableContent.RouteContentType => "Page";

    /// <summary>The page -> routes owned relation.</summary>
    public const string RouteRelation = nameof(PageUpsertViewModel.RouteIds);

    /// <summary>The page -> parent page containment; a root page has none.</summary>
    public const string ParentRelation = nameof(PageUpsertViewModel.ParentNodeId);

    /// <summary>The page's parent page node, read from its version's <see cref="ParentRelation"/>.</summary>
    private static Guid? ParentOf(PageDTO page) => page.Version.TargetOf(ParentRelation);

    public PageModel(
        IContentStore<PageDTO> store,
        IMapper mapper,
        IPageControllerRegistry registry,
        IViewDiscoveryService viewDiscovery,
        IRouteRegistrationService routeRegistration,
        ICMSRouteService routeService,
        IContentZoneService contentZoneService,
        IPageHistoryService pageHistory,
        IContentStore<RouteDTO> routeStore,
        ICmsTransaction transaction,
        ICMSRouteRegistry routeRegistry,
        IChangeSetScope changeSetScope,
        IRelationBinder relationBinder,
        IContentDeleteService deletes)
        : base(changeSetScope, relationBinder, deletes)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _pageHistory = pageHistory ?? throw new ArgumentNullException(nameof(pageHistory));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _routeRegistration = routeRegistration ?? throw new ArgumentNullException(nameof(routeRegistration));
        _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
        _controllerRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
        _contentZoneService = contentZoneService ?? throw new ArgumentNullException(nameof(contentZoneService));
        _routeStore = routeStore ?? throw new ArgumentNullException(nameof(routeStore));
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        _routeRegistry = routeRegistry ?? throw new ArgumentNullException(nameof(routeRegistry));
        _registryHandler = new PageRegistryHandler(
            registry,
            viewDiscovery ?? throw new ArgumentNullException(nameof(viewDiscovery)));
    }

    async Task<IReadOnlyList<CMSRouteDTO>> IRoutableContent.GetRoutesAsync(Guid contentNodeId, CancellationToken ct)
    {
        var routes = await _routeService.GetByOwningContentAsync(contentNodeId, ct);
        return routes.AsReadOnly();
    }

    public async Task<PageIndexViewModel> GetPageIndexAsync(CancellationToken ct = default)
    {
        var pages = await _store.GetAllCurrentDraftsAsync(ct);
        var activeRoutes = await _routeService.GetAllRoutesAsync(ct);
        var publishedNodeIds = await _store.GetPublishedNodeIdsAsync(ct) ?? [];

        // A published page whose drafts (its own fields or its zones) differ from what is live has pending
        // changes; drafts equal to it (left by a discard or a revert) do not.
        var pendingPages = await _pageHistory.GetPagesWithUnpublishedChangesAsync(
            pages.Select(p => p.Version.Node.Id), ct) ?? [];
        return new PageIndexViewModel { Pages = BuildTree(pages, activeRoutes, publishedNodeIds, pendingPages) };
    }

    public async Task<(bool Success, string? ErrorMessage)> SavePageUpsertAsync(PageUpsertViewModel model, CancellationToken ct = default)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        // Page configuration pickers are reference relations owned by the page's version. Lifting them
        // out here means a direct caller gets the same storage as the admin form and MCP.
        var configType = _controllerRegistry.GetByName(model.ControllerName)?.ConfigurationType;
        var (strippedJson, relations) = WidgetConfigurationJson.ExtractRelations(configType, model.ConfigurationJson);
        model.ConfigurationJson = strippedJson;
        return await SavePageUpsertCoreAsync(model, configType, relations, ct);
    }

    private async Task<(bool Success, string? ErrorMessage)> SavePageUpsertCoreAsync(
        PageUpsertViewModel model, Type? configType, IReadOnlyList<RelationValue> relations, CancellationToken ct)
    {
        var dto = _mapper.Map<PageDTO>(model);
        WidgetConfigurationJson.ApplyRelations(dto.Version, configType, relations);

        var note = model.NodeId.HasValue && model.NodeId != Guid.Empty ? "Page edited" : "Page created";
        ContentWriteResult result;
        using (ChangeSetScope.UseChangeNote(note))
            result = await _store.SaveDraftAsync(dto, model.ExpectedVersionNumber, ct);
        if (!result.Success) return (false, result.ErrorMessage ?? "Failed to update page.");
        model.NodeId = result.NodeId;
        await _pageHistory.RecordDraftAsync(result.NodeId, note, ct);
        return (true, null);
    }

    /// <summary>A restored page version is a change to the page set, so it is recorded in page history.</summary>
    public override async Task<AdminSaveResult> RestoreVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        // Opened here (the store would open its own and close it before the history entry is written), so
        // the entry belongs to the restore's change set.
        using var _ = ChangeSetScope.Begin(ChangeSetKind.Restore, null, null);
        var result = await _store.RestoreAsync(versionId, ct);
        if (!result.Success)
            return new AdminSaveResult(false, result.ErrorMessage);

        // The new version's note says which version came back ("Restored from version n").
        var restored = await _store.GetVersionAsync(result.VersionId, ct);
        await _pageHistory.RecordDraftAsync(result.NodeId, $"Page: {restored?.Version.ChangeNote ?? "Restored an earlier version"}", ct);
        return new AdminSaveResult(true);
    }

    /// <summary>
    /// Deletes this page only: its route projection, its zones and the page itself. Its child pages, and
    /// what they own, are deleted first by the relation engine (<see cref="AdminCrudModel{T}.DeleteAsync"/>).
    /// </summary>
    protected override async Task<bool> DeleteSelfAsync(Guid nodeId, CancellationToken ct = default)
    {
        var entity = await _store.GetCurrentDraftAsync(nodeId, ct);
        if (entity != null)
        {
            await _routeRegistration.UnregisterContentRoutesAsync(nodeId, ct);
            await _contentZoneService.DeletePageZonesAsync(nodeId, ct);
        }
        return await _store.DeleteAsync(nodeId, softDelete: false, ct);
    }

    public Task<VersionHistoryViewModel?> GetVersionHistoryAsync(Guid nodeId, CancellationToken ct = default)
        => BuildVersionHistoryAsync(nodeId, ct: ct);

    public async Task<bool> DeletePageVersionAsync(Guid id, CancellationToken ct = default)
        => (await TryDeleteVersionAsync(id, ct)).Success;

    /// <summary>A page version a page-history entry captured cannot be deleted while that entry exists.</summary>
    public override async Task<(bool Success, string? ErrorMessage)> TryDeleteVersionAsync(Guid id, CancellationToken ct = default)
    {
        var entries = await _pageHistory.GetSnapshotsReferencingVersionAsync(id, ct);
        if (entries.Count > 0)
            return (false, PageHistoryMessages.VersionInHistory(entries[0]));

        return await DeleteVersionCoreAsync(id, ct)
            ? (true, null)
            : (false, IAdminCrudHandler.VersionNotDeletedMessage);
    }

    public async Task<(bool Success, string? ErrorMessage, int DescendantsUpdated)> PublishPageAsync(Guid nodeId, CancellationToken ct = default)
    {
        var draft = await _store.GetCurrentDraftAsync(nodeId, ct);
        if (draft == null) return (false, "Page not found.", 0);

        using var _ = ChangeSetScope.Begin(ChangeSetKind.Publish, nodeId, null);

        var parentNodeId = ParentOf(draft);
        var slug = System.Net.WebUtility.UrlDecode(draft.Version.Slug);

        if (!await IsSlugAvailableAsync(slug, parentNodeId, nodeId, ct))
            return (false, "A page with this slug already exists at this location.", 0);

        var previousRoutes = await _routeService.GetByOwningContentAsync(nodeId, ct);
        var routeIds = draft.Version.EffectiveRelations()
            .Where(r => r.Name == RouteRelation)
            .OrderBy(r => r.Ordinal)
            .Select(r => r.TargetNodeId)
            .Distinct()
            .ToList();

        // Pages created before route items existed (and hand-built/test pages) keep the generated
        // route path: derive the pattern and register it directly, exactly as before phase 3.
        if (routeIds.Count == 0)
            return await PublishWithLegacyRouteAsync(nodeId, draft, slug, parentNodeId, previousRoutes, ct);

        // The page, its route items, their projection and every live descendant's moved URLs go live
        // together or not at all: a failure anywhere rolls the whole publish back.
        string? error = null;
        var descendants = 0;
        bool committed;
        try
        {
            committed = await _transaction.RunAsync(async c =>
            {
                var result = await _store.PublishAsync(nodeId, c);
                if (!result.Success)
                {
                    error = result.ErrorMessage;
                    return false;
                }

                var projection = await ProjectRoutesAsync(nodeId, draft, routeIds, previousRoutes, c);
                if (!projection.Success)
                {
                    error = projection.ErrorMessage ?? "Failed to project the page routes.";
                    return false;
                }

                if (projection.DerivedPatternChanged)
                {
                    var moved = await ReprojectDescendantsAsync(nodeId, c);
                    if (!moved.Success)
                    {
                        error = moved.ErrorMessage;
                        return false;
                    }
                    descendants = moved.Count;
                }
                return true;
            }, ct);
        }
        finally
        {
            // The route service invalidates the route cache before this transaction commits, so a reload
            // in between could cache what was there before; invalidate again once the outcome is final.
            _routeRegistry.Invalidate();
        }

        if (!committed)
            return (false, error, 0);

        var zones = await PublishZoneDraftsAsync(nodeId, ct);
        await _pageHistory.CaptureAsync(nodeId, ct);
        return zones.Success
            ? (true, descendants > 0 ? $"{descendants} child URL(s) updated." : null, descendants)
            : (false, zones.ErrorMessage, descendants);
    }

    /// <summary>
    /// Publishes a page with no route items through the route registration service. A failed
    /// registration reverts the publish (unless the page was already live) and restores the old rows.
    /// </summary>
    private async Task<(bool Success, string? ErrorMessage, int DescendantsUpdated)> PublishWithLegacyRouteAsync(
        Guid nodeId, PageDTO draft, string slug, Guid? parentNodeId, IReadOnlyList<CMSRouteDTO> previousRoutes, CancellationToken ct)
    {
        var wasAlreadyPublished = draft.Version.State == ContentVersionState.Published;
        var result = await _store.PublishAsync(nodeId, ct);
        if (!result.Success) return (false, result.ErrorMessage, 0);

        var legacyPattern = await DeriveRoutePatternForPublishAsync(slug, nodeId, parentNodeId, ct);
        var navigationName = previousRoutes.Count > 0
            ? previousRoutes.Select(r => r.NavigationName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
            : draft.Version.Title;

        await _routeRegistration.UnregisterContentRoutesAsync(nodeId, ct);
        var legacy = await _routeRegistration.RegisterContentRoutesAsync(
            this, legacyPattern, draft.ControllerName, nodeId, navigationName, ct);
        if (!legacy.Success)
        {
            if (!wasAlreadyPublished)
                await _store.RevertPublishAsync(nodeId, result.VersionId, result.DisplacedVersionId, ct);

            var legacyRestores = new List<string>();
            foreach (var previous in previousRoutes)
            {
                var restored = await _routeService.UpsertAsync(previous, ct);
                if (!restored.Success)
                    legacyRestores.Add(restored.ErrorMessage ?? $"Route '{previous.Pattern}' could not be restored.");
            }
            var legacyError = legacy.ErrorMessage ?? "Failed to register the page route.";
            return legacyRestores.Count == 0
                ? (false, legacyError, 0)
                : (false, legacyError + " The previous route could not be restored: " + string.Join(" ", legacyRestores), 0);
        }

        var legacyZones = await PublishZoneDraftsAsync(nodeId, ct);
        await _pageHistory.CaptureAsync(nodeId, ct);
        return legacyZones.Success
            ? (true, null, 0)
            : (false, legacyZones.ErrorMessage, 0);
    }

    /// <summary>
    /// Projects the page's published route items into <c>CMSRoutes</c>. Every route item is published,
    /// then upserted by its route node; owner rows no longer related are removed and their route items
    /// deleted (they were detached from the page).
    /// </summary>
    private async Task<(bool Success, string? ErrorMessage, bool DerivedPatternChanged)> ProjectRoutesAsync(
        Guid ownerNodeId, PageDTO page, IReadOnlyList<Guid> routeIds, IReadOnlyList<CMSRouteDTO> previous, CancellationToken ct)
    {
        var previousByRoute = previous
            .Where(r => r.RouteNodeId.HasValue)
            .GroupBy(r => r.RouteNodeId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var projected = new HashSet<Guid>();
        var derivedChanged = false;

        foreach (var routeId in routeIds)
        {
            // A route item can be gone while the page still relates to it (a revert or discard restores a
            // relation whose item an earlier publish deleted): fail rather than drop the page's URL.
            var route = await _routeStore.GetCurrentDraftAsync(routeId, ct);
            if (route == null)
            {
                var label = previousByRoute.TryGetValue(routeId, out var gone) ? gone.Pattern : routeId.ToString();
                return (false, $"Route '{label}' no longer exists; edit the page's routes and publish again.", false);
            }

            var published = await _routeStore.PublishAsync(routeId, ct);
            if (!published.Success)
                return (false, published.ErrorMessage ?? "Failed to publish a route.", false);

            var pattern = route.Mode == RouteMode.Explicit
                ? RoutePatternRules.Normalize(route.Pattern)
                : await DeriveRoutePatternForPublishAsync(page.Version.Slug, ownerNodeId, ParentOf(page), ct);

            if (route.Mode == RouteMode.Derived
                && previousByRoute.TryGetValue(routeId, out var old)
                && !string.Equals(old.Pattern, pattern, StringComparison.OrdinalIgnoreCase))
                derivedChanged = true;

            var row = RouteRow(pattern, ownerNodeId, "Page", routeId, page.ControllerName,
                route.Order, route.NavigationName, route.Mode);
            var upserted = await _routeService.UpsertAsync(row, ct);
            if (!upserted.Success)
                return (false, upserted.ErrorMessage, derivedChanged);

            projected.Add(routeId);
        }

        foreach (var stale in previous.Where(r => !r.RouteNodeId.HasValue || !projected.Contains(r.RouteNodeId.Value)))
        {
            await _routeService.DeleteAsync(stale.Id, ct);
            if (stale.RouteNodeId.HasValue && !projected.Contains(stale.RouteNodeId.Value))
                await _routeStore.DeleteAsync(stale.RouteNodeId.Value, softDelete: false, ct);
        }

        return (true, null, derivedChanged);
    }

    /// <summary>
    /// The data token that marks a projected row's route mode, so a child's derived prefix can pick its
    /// parent's Derived row without loading route items.
    /// </summary>
    internal const string RouteModeToken = "RouteMode";

    private static CMSRouteDTO RouteRow(
        string pattern, Guid ownerNodeId, string ownerType, Guid? routeNodeId,
        string controllerName, int order, string? navigationName, RouteMode mode) => new()
    {
        Pattern = pattern,
        NavigationName = navigationName is { Length: > CMSRouteDTO.NavigationNameMaxLength }
            ? navigationName[..CMSRouteDTO.NavigationNameMaxLength]
            : navigationName,
        DefaultsJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            { "controller", controllerName },
            { "action", "Index" }
        }),
        DataTokensJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            { "RouteContentType", ownerType },
            { RouteModeToken, mode.ToString() }
        }),
        OwningContentNodeId = ownerNodeId,
        OwningContentType = ownerType,
        RouteNodeId = routeNodeId,
        Order = order
    };

    /// <summary>
    /// Re-projects every live descendant's Derived routes after this page's prefix changed, from the
    /// descendant's <b>published</b> page and route versions: a never-published child gets no URL, and a
    /// live child's unpublished slug change stays a draft. Explicit routes are left untouched. Stops at the
    /// first failure (the caller rolls the publish back); otherwise returns how many descendants moved.
    /// </summary>
    private async Task<(bool Success, string? ErrorMessage, int Count)> ReprojectDescendantsAsync(Guid ownerNodeId, CancellationToken ct)
    {
        var pages = await _store.GetAllCurrentDraftsAsync(ct);
        var children = pages
            .Where(p => ParentOf(p).HasValue)
            .GroupBy(p => ParentOf(p)!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var count = 0;
        var queue = new Queue<Guid>();
        queue.Enqueue(ownerNodeId);
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            if (!children.TryGetValue(parent, out var direct))
                continue;

            foreach (var child in direct)
            {
                var childId = child.Version.Node.Id;
                queue.Enqueue(childId);

                var live = Published(await _store.GetAllVersionsAsync(childId, ct));
                if (live == null)
                    continue;

                var routeIds = live.Version.EffectiveRelations()
                    .Where(r => r.Name == RouteRelation)
                    .OrderBy(r => r.Ordinal)
                    .Select(r => r.TargetNodeId)
                    .ToList();
                if (routeIds.Count == 0)
                    continue;

                var previous = await _routeService.GetByOwningContentAsync(childId, ct);
                var previousByRoute = previous.Where(r => r.RouteNodeId.HasValue)
                    .ToDictionary(r => r.RouteNodeId!.Value, r => r.Pattern);
                var changed = false;

                foreach (var routeId in routeIds)
                {
                    var route = Published(await _routeStore.GetAllVersionsAsync(routeId, ct));
                    if (route == null || route.Mode != RouteMode.Derived)
                        continue;

                    var pattern = await DeriveRoutePatternForPublishAsync(
                        live.Version.Slug, childId, ParentOf(live), ct);
                    if (previousByRoute.TryGetValue(routeId, out var old)
                        && !string.Equals(old, pattern, StringComparison.OrdinalIgnoreCase))
                        changed = true;

                    var row = RouteRow(pattern, childId, "Page", routeId, live.ControllerName,
                        route.Order, route.NavigationName, RouteMode.Derived);
                    var upserted = await _routeService.UpsertAsync(row, ct);
                    if (!upserted.Success)
                        return (false, $"The publish was rolled back: the child URL '{pattern}' could not be moved. {upserted.ErrorMessage}".TrimEnd(), count);
                }

                if (changed)
                    count++;
            }
        }
        return (true, null, count);
    }

    /// <summary>The published version among <paramref name="versions"/>, or null when none is live.</summary>
    private static T? Published<T>(List<T>? versions) where T : class, IVersionedContent
        => versions?.FirstOrDefault(v => v.Version.State == ContentVersionState.Published);

    /// <summary>
    /// Publishes the page's pending zone-item drafts (nested zones included) and registers each
    /// published item's widget routes — routes follow what is live, so they change only here.
    /// </summary>
    private async Task<(bool Success, string? ErrorMessage)> PublishZoneDraftsAsync(Guid pageNodeId, CancellationToken ct)
    {
        var errors = await RegisterPublishedZoneRoutesAsync(
            pageNodeId, await _contentZoneService.PublishPageZonesAsync(pageNodeId, ct), ct);

        return errors.Count == 0
            ? (true, null)
            : (false, "Page published; some zone changes failed: " + string.Join(" ", errors));
    }

    public async Task<PageZonePublishResult> PublishZoneItemsAsync(Guid pageNodeId, IReadOnlyCollection<Guid> itemNodeIds, CancellationToken ct = default)
    {
        using var _ = ChangeSetScope.Begin(ChangeSetKind.Publish, pageNodeId, null);

        var zones = await _contentZoneService.PublishPageZoneItemsAsync(pageNodeId, itemNodeIds, ct);
        var errors = await RegisterPublishedZoneRoutesAsync(pageNodeId, zones, ct);
        await _pageHistory.CaptureAsync(pageNodeId, ct);
        return zones with { Errors = errors };
    }

    /// <summary>
    /// Registers each published item's widget routes; returns the publish errors plus any route failures.
    /// </summary>
    private async Task<List<string>> RegisterPublishedZoneRoutesAsync(Guid pageNodeId, PageZonePublishResult zones, CancellationToken ct)
    {
        var errors = zones.Errors.ToList();
        foreach (var item in zones.Published)
        {
            var routes = await _routeRegistration.TryRegisterWidgetRoutesAsync(
                item.ComponentName, item.Version.Node.Id, pageNodeId, item.IsActive, item.MetadataJson, ct);
            if (!routes.Success)
                errors.Add(routes.ErrorMessage ?? $"Failed to register routes for widget '{item.ComponentName}'.");
        }

        return errors;
    }

    public async Task<(bool Success, string? ErrorMessage)> UnpublishPageAsync(Guid nodeId, CancellationToken ct = default)
    {
        var result = await _store.UnpublishAsync(nodeId, ct);
        if (!result.Success) return (false, result.ErrorMessage);

        // The route items stay as drafts; only their projection goes away.
        await _routeRegistration.UnregisterContentRoutesAsync(nodeId, ct);
        return (true, null);
    }

    // IAdminCrudHandler members
    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
        => await GetPageIndexAsync(ct);

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
    {
        if (id.HasValue && id != Guid.Empty)
        {
            var dto = await _store.GetCurrentDraftAsync(id.Value, ct);
            if (dto == null) return null;

            var vm = _mapper.Map<PageUpsertViewModel>(dto);
            // The stored configuration has no relation ids (they are config.* rows on the version): put
            // them back, or the next save of this form would clear them.
            vm.ConfigurationJson = WidgetConfigurationJson.HydrateRelations(
                _controllerRegistry.GetByName(dto.ControllerName)?.ConfigurationType, vm.ConfigurationJson, dto.Version.Relations);
            await PopulateParentageAsync(vm, dto, ct);
            return vm;
        }

        var newVm = new PageUpsertViewModel();
        var parentRoute = query["parentRoute"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(parentRoute))
        {
            parentRoute = parentRoute.TrimEnd('/');
            if (!parentRoute.StartsWith('/'))
                parentRoute = "/" + parentRoute;
            newVm.ParentRoutePrefix = parentRoute;

            var parentNodeId = await ResolveParentNodeIdAsync(parentRoute, ct);
            if (parentNodeId.HasValue)
                newVm.ParentNodeId = parentNodeId.Value;
        }
        return newVm;
    }

    public override object CreateEmptyUpsertViewModel() => new PageUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (PageUpsertViewModel)model;

        var excludeNodeId = vm.NodeId.HasValue && vm.NodeId != Guid.Empty ? vm.NodeId : null;
        var effectiveSlug = SlugNormalizer.Normalize(string.IsNullOrWhiteSpace(vm.Slug) ? vm.Title : vm.Slug);

        var slugAvailable = await IsSlugAvailableAsync(effectiveSlug, vm.ParentNodeId, excludeNodeId, ct);
        if (!slugAvailable)
            return new AdminSaveResult(false, "A page with this slug already exists at this location.", "Slug");

        var controllerInfo = _controllerRegistry.GetByName(vm.ControllerName);
        if (controllerInfo == null)
            return new AdminSaveResult(false, $"Unknown controller: {vm.ControllerName}", "ControllerName");

        var configType = controllerInfo.ConfigurationType;
        var (strippedJson, relations) = WidgetConfigurationJson.ExtractRelations(configType, vm.ConfigurationJson);
        vm.ConfigurationJson = strippedJson;

        var relationError = await ValidateConfigRelationsAsync(configType, relations, ct);
        if (relationError != null)
            return relationError;

        var validationErrors = _controllerRegistry.ValidateConfiguration(vm.ControllerName, vm.ConfigurationJson);
        if (validationErrors.Count > 0)
            return new AdminSaveResult(false, string.Join(" ", validationErrors), "ConfigurationJson");

        // A route is an owned reference, so it has one owner: another page's route can't be taken over.
        foreach (var routeId in vm.RouteIds.Distinct())
        {
            var owners = await _store.FindCurrentDraftsRelatedAsync(RouteRelation, routeId, ct) ?? [];
            if (owners.Any(p => p.Version.Node.Id != excludeNodeId))
                return new AdminSaveResult(false, "Routes: a selected route belongs to another page.", nameof(PageUpsertViewModel.RouteIds));
        }

        // A new page owns at least one route: mint its Derived route draft and relate it, so the
        // form shows a route immediately and publish has something to project.
        Guid? mintedRouteId = null;
        if ((vm.NodeId == null || vm.NodeId == Guid.Empty) && vm.RouteIds.Count == 0)
        {
            var minted = await _routeStore.SaveDraftAsync(
                new RouteDTO { Mode = RouteMode.Derived, NavigationName = vm.Title }, null, ct);
            if (!minted.Success)
                return new AdminSaveResult(false, minted.ErrorMessage ?? "Unable to create the page route.");
            mintedRouteId = minted.NodeId;
            vm.RouteIds = [minted.NodeId];
        }

        var result = await SavePageUpsertCoreAsync(vm, configType, relations, ct);
        if (!result.Success && mintedRouteId.HasValue)
            await _routeStore.DeleteAsync(mintedRouteId.Value, softDelete: false, ct);

        return result.Success
            ? new AdminSaveResult(true, NodeId: vm.NodeId)
            : new AdminSaveResult(false, result.ErrorMessage);
    }

    /// <summary>Validates a page configuration's extracted relations as its own declarations.</summary>
    private async Task<AdminSaveResult?> ValidateConfigRelationsAsync(
        Type? configType, IReadOnlyList<RelationValue> relations, CancellationToken ct)
    {
        if (configType == null)
            return null;

        var config = Activator.CreateInstance(configType)!;
        RelationProperties.Write(config, relations, ContentRelationRegistry.ConfigPrefix);
        return await RelationBinder.ValidateAsync(config, DisplayName, ct);
    }

    public override Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default)
        => GetPickerListAsync(null, ct);

    public override Task<IEnumerable<object>> GetApiListExcludingSubtreeAsync(Guid excludeSubtreeOf, CancellationToken ct = default)
        => GetPickerListAsync(excludeSubtreeOf, ct);

    /// <summary>
    /// Every page at every depth as <c>{ id, title, path }</c>, parent first and siblings by path, so the
    /// Parent Page picker offers nested pages and tells same-titled ones apart. Built from the current
    /// drafts and their parent relation (not the URL tree, which merges pages sharing a path), and leaving
    /// out <paramref name="excludeSubtreeOf"/> and its descendants, which can't become its parent.
    /// </summary>
    private async Task<IEnumerable<object>> GetPickerListAsync(Guid? excludeSubtreeOf, CancellationToken ct)
    {
        var pages = await _store.GetAllCurrentDraftsAsync(ct);
        var routeMap = (await _routeService.GetAllRoutesAsync(ct))
            .Where(r => r.OwningContentType == "Page" && r.OwningContentNodeId.HasValue)
            .GroupBy(r => r.OwningContentNodeId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var paths = BuildPageRouteMap(pages, routeMap);

        var byPath = pages.OrderBy(p => paths[p.Version.Node.Id], StringComparer.OrdinalIgnoreCase).ToList();
        var ids = pages.Select(p => p.Version.Node.Id).ToHashSet();
        var children = byPath
            .Where(p => ParentOf(p) is { } parent && ids.Contains(parent))
            .GroupBy(p => ParentOf(p)!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Pre-order walk from the roots (no parent, or a parent that is gone). Pages caught in a parent
        // cycle are reachable from no root, so a second pass walks from each page not yet listed.
        var ordered = new List<PageDTO>();
        var seen = new HashSet<Guid>();
        var roots = byPath.Where(p => !(ParentOf(p) is { } parent && ids.Contains(parent)));
        foreach (var start in roots.Concat(byPath))
        {
            var stack = new Stack<PageDTO>([start]);
            while (stack.Count > 0)
            {
                var page = stack.Pop();
                if (!seen.Add(page.Version.Node.Id))
                    continue;
                ordered.Add(page);
                foreach (var child in Enumerable.Reverse(children.GetValueOrDefault(page.Version.Node.Id) ?? []))
                    stack.Push(child);
            }
        }

        var excluded = excludeSubtreeOf is { } root ? SubtreeOf(root, children) : [];
        return ordered
            .Where(p => !excluded.Contains(p.Version.Node.Id))
            .Select(p => (object)new { id = p.Version.Node.Id, title = p.Version.Title, path = paths[p.Version.Node.Id] })
            .ToList();
    }

    /// <summary>The page and its descendants through the parent relation (a cycle counts each page once).</summary>
    private static HashSet<Guid> SubtreeOf(Guid root, Dictionary<Guid, List<PageDTO>> children)
    {
        var subtree = new HashSet<Guid> { root };
        var queue = new Queue<Guid>([root]);
        while (queue.Count > 0)
        {
            foreach (var child in children.GetValueOrDefault(queue.Dequeue()) ?? [])
            {
                if (subtree.Add(child.Version.Node.Id))
                    queue.Enqueue(child.Version.Node.Id);
            }
        }
        return subtree;
    }

    public override async Task<AdminSaveResult> PublishAsync(Guid nodeId, CancellationToken ct = default)
    {
        var result = await PublishPageAsync(nodeId, ct);
        return result.Success
            ? new AdminSaveResult(true, result.ErrorMessage)
            : new AdminSaveResult(false, result.ErrorMessage);
    }

    public override async Task<AdminSaveResult> UnpublishAsync(Guid nodeId, CancellationToken ct = default)
    {
        var result = await UnpublishPageAsync(nodeId, ct);
        return result.Success
            ? new AdminSaveResult(true)
            : new AdminSaveResult(false, result.ErrorMessage);
    }

    public override async Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
    {
        var loaded = await LoadRestoreVersionAsync(historicalId, ct);
        if (loaded == null) return null;
        var vm = _mapper.Map<PageUpsertViewModel>(loaded.Value.Historical);
        vm.ExpectedVersionNumber = loaded.Value.CurrentVersionNumber;
        await PopulateParentageAsync(vm, loaded.Value.Historical, ct);
        return vm;
    }

    public override Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default)
        => DeletePageVersionAsync(id, ct);

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static List<PageTreeNode> BuildTree(List<PageDTO> pages, List<CMSRouteDTO> activeRoutes, HashSet<Guid> publishedNodeIds, HashSet<Guid> pendingPages)
    {
        var routeMap = activeRoutes
            .Where(r => r.OwningContentType == "Page" && r.OwningContentNodeId.HasValue)
            .GroupBy(r => r.OwningContentNodeId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var pageRouteMap = BuildPageRouteMap(pages, routeMap);

        var sortedPages = pages
            .Where(p => pageRouteMap.ContainsKey(p.Version.Node.Id))
            .OrderBy(p => pageRouteMap[p.Version.Node.Id])
            .ToList();

        var roots = new List<PageTreeNode>();
        var nodeMap = new Dictionary<string, PageTreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var page in sortedPages)
        {
            if (!pageRouteMap.TryGetValue(page.Version.Node.Id, out var currentRoute))
                continue;

            if (currentRoute == "/")
            {
                if (!nodeMap.TryGetValue("/", out var rootNode))
                {
                    rootNode = new PageTreeNode
                    {
                        Path = "/",
                        Title = page.Version.Title,
                        PageNodeId = page.Version.Node.Id,
                        IsPublished = IsNodePublished(page.Version.Node.Id, publishedNodeIds),
                        HasPendingChanges = HasPendingChanges(page.Version.Node.Id, publishedNodeIds, pendingPages),
                        IsHidden = page.Version.Node.IsHidden,
                        ModificationDate = page.Version.CreatedUtc
                    };
                    nodeMap["/"] = rootNode;
                    roots.Insert(0, rootNode);
                }
                else
                {
                    rootNode.Title = page.Version.Title;
                    rootNode.PageNodeId = page.Version.Node.Id;
                    rootNode.IsPublished = IsNodePublished(page.Version.Node.Id, publishedNodeIds);
                    rootNode.HasPendingChanges = HasPendingChanges(page.Version.Node.Id, publishedNodeIds, pendingPages);
                    rootNode.IsHidden = page.Version.Node.IsHidden;
                    rootNode.ModificationDate = page.Version.CreatedUtc;
                }
                continue;
            }

            var segments = currentRoute.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var currentPath = "";

            for (int i = 0; i < segments.Length; i++)
            {
                currentPath = "/" + string.Join("/", segments.Take(i + 1));
                var isLeaf = i == segments.Length - 1;

                if (!nodeMap.TryGetValue(currentPath, out var node))
                {
                    node = new PageTreeNode
                    {
                        Path = currentPath,
                        Title = isLeaf ? page.Version.Title : segments[i],
                        PageNodeId = isLeaf ? page.Version.Node.Id : null,
                        IsPublished = isLeaf && IsNodePublished(page.Version.Node.Id, publishedNodeIds),
                        HasPendingChanges = isLeaf && HasPendingChanges(page.Version.Node.Id, publishedNodeIds, pendingPages),
                        IsHidden = isLeaf && page.Version.Node.IsHidden,
                        ModificationDate = isLeaf ? page.Version.CreatedUtc : null
                    };
                    nodeMap[currentPath] = node;

                    if (i == 0)
                    {
                        roots.Add(node);
                    }
                    else
                    {
                        var parentPath = "/" + string.Join("/", segments.Take(i));
                        if (nodeMap.TryGetValue(parentPath, out var parentNode))
                        {
                            parentNode.Children.Add(node);
                        }
                    }
                }
                else if (isLeaf)
                {
                    node.Title = page.Version.Title;
                    node.PageNodeId = page.Version.Node.Id;
                    node.IsPublished = IsNodePublished(page.Version.Node.Id, publishedNodeIds);
                    node.HasPendingChanges = HasPendingChanges(page.Version.Node.Id, publishedNodeIds, pendingPages);
                    node.IsHidden = page.Version.Node.IsHidden;
                    node.ModificationDate = page.Version.CreatedUtc;
                }
            }
        }

        var descendants = CountDescendants(pages);
        foreach (var node in nodeMap.Values)
        {
            if (node.PageNodeId is { } id)
                node.DescendantCount = descendants.GetValueOrDefault(id);
        }

        return roots;
    }

    /// <summary>
    /// Each page's descendant pages at every depth, through the parent relation of the current drafts
    /// (what a delete takes with it), whatever their URLs. A parent cycle counts each page once.
    /// </summary>
    private static Dictionary<Guid, int> CountDescendants(List<PageDTO> pages)
    {
        var children = pages
            .Where(p => ParentOf(p).HasValue)
            .GroupBy(p => ParentOf(p)!.Value)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Version.Node.Id).ToList());

        var counts = new Dictionary<Guid, int>();
        foreach (var page in pages)
        {
            var root = page.Version.Node.Id;
            var seen = new HashSet<Guid> { root };
            var queue = new Queue<Guid>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                foreach (var child in children.GetValueOrDefault(queue.Dequeue()) ?? [])
                {
                    if (seen.Add(child))
                        queue.Enqueue(child);
                }
            }
            counts[root] = seen.Count - 1;
        }
        return counts;
    }

    private static Dictionary<Guid, string> BuildPageRouteMap(List<PageDTO> pages, Dictionary<Guid, CMSRouteDTO> routeMap)
    {
        var pageRouteMap = new Dictionary<Guid, string>();

        // Pages with a route row use its pattern directly.
        foreach (var page in pages)
        {
            if (routeMap.TryGetValue(page.Version.Node.Id, out var route))
                pageRouteMap[page.Version.Node.Id] = route.Pattern;
        }

        // Pages with no route row (unpublished) derive their path by walking their parent relation, so
        // unpublished children nest under their parent instead of surfacing at root.
        var unresolved = pages.Where(p => !pageRouteMap.ContainsKey(p.Version.Node.Id)).ToList();
        while (unresolved.Count > 0)
        {
            var progressed = false;
            foreach (var page in unresolved.ToList())
            {
                var parentId = ParentOf(page);
                if (parentId.HasValue && pageRouteMap.TryGetValue(parentId.Value, out var parentPath))
                {
                    pageRouteMap[page.Version.Node.Id] = CombinePath(parentPath, System.Net.WebUtility.UrlDecode(page.Version.Slug));
                    unresolved.Remove(page);
                    progressed = true;
                }
            }

            if (!progressed)
                break;
        }

        foreach (var page in unresolved)
            pageRouteMap[page.Version.Node.Id] = DerivePatternFromSlug(page.Version.Slug);

        return pageRouteMap;
    }

    private static string CombinePath(string parentPath, string slug)
    {
        var trimmed = parentPath.TrimEnd('/');
        return (trimmed.Length == 0 ? string.Empty : trimmed) + "/" + slug;
    }

    private static bool IsNodePublished(Guid nodeId, HashSet<Guid> publishedNodeIds)
        => publishedNodeIds.Contains(nodeId);

    private static bool HasPendingChanges(Guid nodeId, HashSet<Guid> publishedNodeIds, HashSet<Guid> pendingPages)
        => publishedNodeIds.Contains(nodeId) && pendingPages.Contains(nodeId);

    private async Task<string> DeriveRoutePatternForPublishAsync(string slug, Guid nodeId, Guid? parentNodeId, CancellationToken ct)
    {
        var prefix = await GetParentRoutePrefixAsync(parentNodeId, ct);

        if (prefix == null)
        {
            var existingRoutes = await _routeService.GetByOwningContentAsync(nodeId, ct);
            var existing = PrefixRoute(existingRoutes);
            if (existing != null)
            {
                var lastSlash = existing.Pattern.LastIndexOf('/');
                prefix = lastSlash > 0 ? existing.Pattern[..lastSlash] : null;
            }
        }

        return DeriveRoutePatternFromSlug(System.Net.WebUtility.UrlDecode(slug), prefix);
    }

    private static string DeriveRoutePatternFromSlug(string slug, string? parentRoutePrefix)
    {
        if (!string.IsNullOrWhiteSpace(parentRoutePrefix))
        {
            parentRoutePrefix = parentRoutePrefix.TrimEnd('/');
            return parentRoutePrefix + "/" + slug;
        }
        if (string.Equals(slug, "home", StringComparison.OrdinalIgnoreCase))
            return "/";
        return "/" + slug;
    }

    private static string DerivePatternFromSlug(string slug)
    {
        var decoded = System.Net.WebUtility.UrlDecode(slug);
        if (string.Equals(decoded, "home", StringComparison.OrdinalIgnoreCase))
            return "/";
        return "/" + decoded;
    }

    private async Task<bool> IsSlugAvailableAsync(string? slug, Guid? parentNodeId, Guid? excludeNodeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return false;

        var normalizedSlug = SlugNormalizer.Normalize(slug);

        var siblings = (parentNodeId.HasValue
            ? await _store.FindCurrentDraftsRelatedAsync(ParentRelation, parentNodeId.Value, ct)
            : await _store.FindCurrentDraftsWithoutRelationAsync(ParentRelation, ct)) ?? [];
        if (siblings.Any(p =>
                (excludeNodeId == null || p.Version.Node.Id != excludeNodeId.Value)
             && SlugNormalizer.Normalize(System.Net.WebUtility.UrlDecode(p.Version.Slug)) == normalizedSlug))
            return false;

        var prefix = await GetParentRoutePrefixAsync(parentNodeId, ct);
        var routePattern = DeriveRoutePatternFromSlug(slug, prefix);
        return await _routeService.IsPatternAvailableAsync(routePattern, excludeNodeId, null, ct);
    }

    private async Task<string?> GetParentRoutePrefixAsync(Guid? parentNodeId, CancellationToken ct)
    {
        if (!parentNodeId.HasValue)
            return null;

        var routes = await _routeService.GetByOwningContentAsync(parentNodeId.Value, ct);
        return PrefixRoute(routes)?.Pattern;
    }

    /// <summary>
    /// The row a page's URL derives from when it owns several: its Derived route (a row with no route
    /// item is a generated one, so it counts as derived), else the first by order, then pattern.
    /// </summary>
    private static CMSRouteDTO? PrefixRoute(IEnumerable<CMSRouteDTO> routes)
        => routes
            .OrderBy(r => IsDerivedRow(r) ? 0 : 1)
            .ThenBy(r => r.Order)
            .ThenBy(r => r.Pattern, StringComparer.Ordinal)
            .FirstOrDefault();

    private static bool IsDerivedRow(CMSRouteDTO route)
        => route.RouteNodeId == null
        || route.DataTokensJson.Contains($"\"{RouteModeToken}\":\"{RouteMode.Derived}\"", StringComparison.Ordinal);

    private async Task<Guid?> ResolveParentNodeIdAsync(string pattern, CancellationToken ct)
    {
        var routes = await _routeService.GetAllRoutesAsync(ct) ?? [];
        var direct = routes.FirstOrDefault(r => r.Pattern == pattern && r.OwningContentNodeId.HasValue);
        if (direct != null)
            return direct.OwningContentNodeId;

        // No route row for this path: the parent is unpublished. Walk the current drafts through the
        // same path-derivation the admin tree uses so a child can still nest under an unpublished parent.
        var pages = await _store.GetAllCurrentDraftsAsync(ct) ?? [];
        var routeMap = routes
            .Where(r => r.OwningContentType == "Page" && r.OwningContentNodeId.HasValue)
            .GroupBy(r => r.OwningContentNodeId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var pageRouteMap = BuildPageRouteMap(pages, routeMap);

        var match = pageRouteMap.FirstOrDefault(kv => kv.Value == pattern);
        return match.Key == Guid.Empty ? null : match.Key;
    }

    private async Task PopulateParentageAsync(PageUpsertViewModel vm, PageDTO dto, CancellationToken ct)
    {
        var parentNodeId = ParentOf(dto);
        if (!parentNodeId.HasValue)
            return;

        vm.ParentNodeId = parentNodeId.Value;
        var parentRoutes = await _routeService.GetByOwningContentAsync(parentNodeId.Value, ct);
        vm.ParentRoutePrefix = parentRoutes.FirstOrDefault()?.Pattern;
    }
}

internal sealed class PageRegistryHandler : IAdminRegistryHandler
{
    private readonly IPageControllerRegistry _registry;
    private readonly IViewDiscoveryService _viewDiscovery;

    public PageRegistryHandler(IPageControllerRegistry registry, IViewDiscoveryService viewDiscovery)
    {
        _registry = registry;
        _viewDiscovery = viewDiscovery ?? throw new ArgumentNullException(nameof(viewDiscovery));
    }

    public IActionResult GetAll()
    {
        var controllers = _registry.GetAllControllers().Select(c => new
        {
            name = c.Name,
            displayName = c.DisplayName,
            description = c.Description,
            category = c.Category
        }).ToList();

        return new JsonResult(controllers);
    }

    public IActionResult GetProperties(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new BadRequestObjectResult(new { error = "Controller name is required." });

        var controller = _registry.GetByName(name);
        if (controller == null)
            return new NotFoundObjectResult(new { error = $"Controller '{name}' not found." });

        var properties = controller.Properties.Select(p => new
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
            dropdownOptions = p.DropdownOptions,
            viewComponentName = p.ViewComponentName,
            min = p.Min,
            max = p.Max,
            maxLength = p.MaxLength
        }).OrderBy(p => p.order).ToList();

        var availableViews = _viewDiscovery.GetControllerViews(name);

        return new JsonResult(new
        {
            controllerName = controller.Name,
            displayName = controller.DisplayName,
            category = controller.Category,
            availableViews,
            properties
        });
    }

    public IActionResult GetForm(string name, string? valuesJson)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new BadRequestObjectResult(new { error = "Controller name is required." });

        var controller = _registry.GetByName(name);
        if (controller == null)
            return new NotFoundObjectResult(new { error = $"Controller '{name}' not found." });

        object? instance = null;
        if (controller.ConfigurationType != null)
            instance = WebWayCMS.Forms.DynamicConfigurationForm.Materialize(controller.ConfigurationType, valuesJson);

        return WebWayCMS.Forms.DynamicConfigurationForm.Render(instance);
    }
}
