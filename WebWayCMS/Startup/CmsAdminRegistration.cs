using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using WebWayCMS.Controllers.Admin;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Routing;
using WebWayCMS.Services.ContentSeeding;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsAdminRegistration
{
    internal static void MapAdminTypes(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RouteOptions>(o => o.ConstraintMap["notreserved"] = typeof(NotReservedConstraint));
        services.AddScoped<IAdminHandlerRegistry, AdminHandlerRegistry>();

        // Replace the rendering-only published read context with the preview-aware one.
        services.AddScoped<WebWayCMS.Data.Services.IContentReadContext, PreviewAwareReadContext>();

        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<Models.ContentBlock.ContentBlockModel>());
        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<Models.Page.PageModel>());
        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<Models.Article.ArticleListModel>());
        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<Models.ContentZone.ContentZoneModel>());
        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<Models.WidgetRegistration.WidgetRegistrationModel>());
        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<Models.PageControllerRegistration.PageControllerRegistrationModel>());
        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<Models.CMSRoute.CMSRouteModel>());
        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<Models.FormComponentRegistration.FormComponentRegistrationModel>());

        // JSON content seeding (admin mode only — a rendering-only host has no IAdminHandlerRegistry).
        services.Configure<ContentSeedOptions>(configuration.GetSection(ContentSeedOptions.SectionName));
        services.AddScoped<WebWayCMS.Data.Services.IContentSeedRecordService, WebWayCMS.Data.Services.ContentSeedRecordService>();
        services.AddScoped<IContentSeedSourceProvider>(sp => new AssemblyContentSeedSourceProvider(
            CmsStartupHelpers.SeedAssemblies(sp).Concat(sp.GetService<CmsContentSeedCatalog>()?.Assemblies ?? []).Distinct(),
            sp.GetRequiredService<IOptions<ContentSeedOptions>>()));
        services.AddScoped<IContentSeedSourceProvider>(sp => new FileContentSeedSourceProvider(
            sp.GetRequiredService<IWebHostEnvironment>(),
            sp.GetRequiredService<IOptions<ContentSeedOptions>>(),
            sp.GetService<CmsContentSeedCatalog>()?.Files ?? []));
        services.AddScoped<IJsonContentSeeder, JsonContentSeeder>();

        services.Configure<MvcOptions>(_ => { });
        services.AddControllersWithViews().ConfigureApplicationPartManager(apm =>
        {
            var adminAsm = typeof(AdminContentController).Assembly;
            if (!apm.ApplicationParts.Any(p => p.Name == adminAsm.GetName().Name))
            {
                apm.ApplicationParts.Add(new AssemblyPart(adminAsm));
                apm.ApplicationParts.Add(new CompiledRazorAssemblyPart(adminAsm));
            }
        });
    }
}
