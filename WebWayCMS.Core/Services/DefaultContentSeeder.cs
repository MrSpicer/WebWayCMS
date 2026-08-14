using System.Text.Json;

using Serilog;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Services;

public sealed class DefaultContentSeeder : IDefaultContentSeeder
{
    private readonly IContentStore<PageDTO> _pageStore;
    private readonly ICMSRouteService _routeService;

    public DefaultContentSeeder(IContentStore<PageDTO> pageStore, ICMSRouteService routeService)
    {
        _pageStore = pageStore ?? throw new ArgumentNullException(nameof(pageStore));
        _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
    }

    public async Task SeedDefaultPagesAsync(bool seedAdminPage, CancellationToken ct = default)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("WEBWAYCMS_SKIP_DEFAULTPAGE"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Log.ForContext<DefaultContentSeeder>().Information("Skipping default home page seeding due to WEBWAYCMS_SKIP_DEFAULTPAGE=true");
            return;
        }

        var logger = Log.ForContext<DefaultContentSeeder>();
        var existingHomeRoute = await _routeService.MatchRouteAsync("/", ct);

        if (existingHomeRoute == null)
        {
            logger.Information("No page was found in database. Creating default Home page at route '/'.");

            var homePage = new PageDTO
            {
                ConfigurationJson = "{}",
                Version = new ContentVersion
                {
                    Title = "Home",
                    Slug = "home"
                }
            };

            await _pageStore.SaveDraftAsync(homePage, null, ct);
            var homeNodeId = homePage.Version.Node!.Id;
            await _pageStore.PublishAsync(homeNodeId, ct);
            logger.Information("Created default Home page with node ID {PageId}", homeNodeId);

            await SeedRouteAsync("/", "GenericPage", "Page", homeNodeId, ct, logger, "Home");
        }
        else
        {
            logger.Debug("Pages already exist, skipping default home page creation.");
        }

        if (seedAdminPage)
        {
            var existingAdminRoute = await _routeService.MatchRouteAsync("/wadmin", ct);
            if (existingAdminRoute == null)
            {
                await SeedAdminPageAsync(ct, logger);
            }
            else
            {
                logger.Debug("Admin page already exists, skipping default admin page creation.");
            }
        }
    }

    private async Task SeedAdminPageAsync(CancellationToken ct, ILogger logger)
    {
        var adminPage = new PageDTO
        {
            ConfigurationJson = "{}",
            ViewName = "Dashboard",
            Version = new ContentVersion
            {
                Title = "Dashboard",
                Slug = "wadmin"
            }
        };

        await _pageStore.SaveDraftAsync(adminPage, null, ct);
        var adminNodeId = adminPage.Version.Node!.Id;
        await _pageStore.PublishAsync(adminNodeId, ct);
        logger.Information("Created default Dashboard page with node ID {PageId}", adminNodeId);

        await SeedRouteAsync("/wadmin", "GenericAdminPage", "Page", adminNodeId, ct, logger, "Dashboard");
    }

    private async Task SeedRouteAsync(
        string pattern, string controllerName, string owningContentType,
        Guid contentNodeId, CancellationToken ct, ILogger logger, string label)
    {
        var defaults = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            { "controller", controllerName },
            { "action", "Index" }
        });

        var dataTokens = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            { "ConfigurationJson", "{}" },
            { "RouteContentType", owningContentType }
        });

        var route = new CMSRouteDTO
        {
            Pattern = pattern,
            DefaultsJson = defaults,
            DataTokensJson = dataTokens,
            OwningContentNodeId = contentNodeId,
            OwningContentType = owningContentType
        };

        await _routeService.UpsertAsync(route, ct);
        logger.Information("Created default {Label} route at pattern '{Pattern}'", label, pattern);
    }
}
