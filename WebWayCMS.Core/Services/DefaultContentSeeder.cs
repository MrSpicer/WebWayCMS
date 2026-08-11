using System.Text.Json;

using Serilog;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Services;

public sealed class DefaultContentSeeder : IDefaultContentSeeder
{
    private readonly IPageService _pageService;
    private readonly ICMSRouteService _routeService;

    public DefaultContentSeeder(IPageService pageService, ICMSRouteService routeService)
    {
        _pageService = pageService ?? throw new ArgumentNullException(nameof(pageService));
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

            var now = DateTime.UtcNow;
            var pageId = Guid.NewGuid();

            var homePage = new PageDTO
            {
                ConfigurationJson = "{}",
                ContentMeta = new ContentDTO
                {
                    Id = pageId,
                    Title = "Home",
                    Slug = "home",
                    IsPublished = true,
                    PublicationDate = now,
                    CreationDate = now,
                    ModificationDate = now,
                    CreatedBy = Guid.Empty,
                    LastModifiedBy = Guid.Empty
                }
            };

            var created = await _pageService.CreateAsync(homePage, ct);
            logger.Information("Created default Home page with ID {PageId}", created.ContentMeta.Id);

            await SeedRouteAsync("/", "GenericPage", null, "Page", created.ContentMeta, ct, logger, "Home");
        }
        else
        {
            logger.Debug("Pages already exist, skipping default home page creation.");
        }

        if (seedAdminPage)
        {
            var existingAdminRoute = await _routeService.MatchRouteAsync("/admin", ct);
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
        var now = DateTime.UtcNow;
        var adminPageId = Guid.NewGuid();

        var adminPage = new PageDTO
        {
            ConfigurationJson = "{}",
            ViewName = "Dashboard",
            ContentMeta = new ContentDTO
            {
                Id = adminPageId,
                Title = "Admin",
                Slug = "admin",
                IsPublished = true,
                PublicationDate = now,
                CreationDate = now,
                ModificationDate = now,
                CreatedBy = Guid.Empty,
                LastModifiedBy = Guid.Empty
            }
        };

        var created = await _pageService.CreateAsync(adminPage, ct);
        logger.Information("Created default Admin page with ID {PageId}", created.ContentMeta.Id);

        await SeedRouteAsync("/admin", "GenericAdminPage", null, "Page", created.ContentMeta, ct, logger, "Admin");
    }

    private async Task SeedRouteAsync(
        string pattern, string controllerName, string? viewName, string owningContentType,
        ContentDTO owningContentMeta, CancellationToken ct, ILogger logger, string label)
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
            OwningContentMasterId = owningContentMeta.MasterId,
            OwningContentType = owningContentType,
            ContentMeta = new ContentDTO
            {
                Id = Guid.NewGuid(),
                Title = $"{label} Route",
                Slug = pattern.TrimStart('/'),
                IsPublished = true,
                PublicationDate = DateTime.UtcNow,
                CreationDate = DateTime.UtcNow,
                ModificationDate = DateTime.UtcNow,
                CreatedBy = Guid.Empty,
                LastModifiedBy = Guid.Empty
            }
        };

        await _routeService.UpsertAsync(route, ct);
        logger.Information("Created default {Label} route at pattern '{Pattern}'", label, pattern);
    }
}
