using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Controllers.Admin;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Routing;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsAdminRegistration
{
    internal static void MapAdminTypes(IServiceCollection services)
    {
        services.Configure<RouteOptions>(o => o.ConstraintMap["notreserved"] = typeof(NotReservedConstraint));
        services.AddScoped<IAdminHandlerRegistry, AdminHandlerRegistry>();

        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<Models.ContentBlock.ContentBlockModel>());
        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<Models.Page.PageModel>());
        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<Models.Article.ArticleListModel>());
        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<Models.ContentZone.ContentZoneModel>());
        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<Models.WidgetRegistration.WidgetRegistrationModel>());
        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<Models.PageControllerRegistration.PageControllerRegistrationModel>());
        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<Models.CMSRoute.CMSRouteModel>());
        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<Models.FormComponentRegistration.FormComponentRegistrationModel>());

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
