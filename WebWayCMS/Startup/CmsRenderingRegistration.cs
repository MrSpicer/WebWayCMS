using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.ContentZones;
using WebWayCMS.Controllers;
using WebWayCMS.Data;
using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;
using WebWayCMS.Interfaces;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Article;
using WebWayCMS.Models.CMSRoute;
using WebWayCMS.Models.ContentBlock;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Models.FormComponentRegistration;
using WebWayCMS.Models.Page;
using WebWayCMS.Models.PageControllerRegistration;
using WebWayCMS.Models.WidgetRegistration;
using WebWayCMS.Pages;
using WebWayCMS.Routing;
using WebWayCMS.Services;
using WebWayCMS.TagHelpers;
using WebWayCMS.ViewComponents;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsRenderingRegistration
{
    internal static void AddRenderingCoreTypes(IServiceCollection services)
    {
#if DEBUG
        services.AddSingleton<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, DevEmailSender>();
#endif
        services.AddHttpContextAccessor();
        services.AddSingleton<UserService>();

        services.AddScoped<IViewDiscoveryService, ViewDiscoveryService>();

        services.AddSingleton<IWidgetRegistry, WidgetRegistry>();
        services.AddSingleton<IFormComponentRegistry, FormComponentRegistry>();
        services.AddScoped<IFormComponentResolver, FormComponentResolver>();

        AddContentServices(services);
        AddRoutingServices(services);
        AddDomainModels(services);
        AddMvcApplicationParts(services);
    }

    private static void AddContentServices(IServiceCollection services)
    {
        services.AddScoped<IContentService<ArticleDTO>>(sp =>
        {
            var ctx = sp.GetRequiredService<CmsDbContext>();
            return new ContentService<ArticleDTO>(ctx);
        });

        services.AddScoped<IContentService<ArticleListDTO>>(sp =>
        {
            var ctx = sp.GetRequiredService<CmsDbContext>();
            return new ContentService<ArticleListDTO>(ctx);
        });

        services.AddScoped<IContentService<ContentBlockDTO>>(sp =>
        {
            var ctx = sp.GetRequiredService<CmsDbContext>();
            return new ContentService<ContentBlockDTO>(ctx);
        });

        services.AddScoped<IContentZoneService, ContentZoneService>();
        services.AddScoped<IPageService, PageService>();
        services.AddScoped<IWidgetRegistrationService, WidgetRegistrationService>();
        services.AddScoped<IPageControllerRegistrationService, PageControllerRegistrationService>();

        services.AddScoped<IFormComponentRegistrationService, FormComponentRegistrationService>();

        services.AddScoped<IContentService<WidgetRegistrationDTO>>(sp =>
        {
            var ctx = sp.GetRequiredService<CmsDbContext>();
            return new ContentService<WidgetRegistrationDTO>(ctx);
        });

        services.AddScoped<IContentService<PageControllerRegistrationDTO>>(sp =>
        {
            var ctx = sp.GetRequiredService<CmsDbContext>();
            return new ContentService<PageControllerRegistrationDTO>(ctx);
        });

        services.AddScoped<IContentService<CMSRouteDTO>>(sp =>
        {
            var ctx = sp.GetRequiredService<CmsDbContext>();
            return new ContentService<CMSRouteDTO>(ctx);
        });

        services.AddScoped<IContentService<FormComponentRegistrationDTO>>(sp =>
        {
            var ctx = sp.GetRequiredService<CmsDbContext>();
            return new ContentService<FormComponentRegistrationDTO>(ctx);
        });
    }

    private static void AddRoutingServices(IServiceCollection services)
    {
        services.AddSingleton<IPageControllerRegistry, PageControllerRegistry>();
        services.AddSingleton<ICMSRouteRegistry, CMSRouteRegistry>();
        services.AddScoped<ICMSRouteService, CMSRouteService>();
        services.AddScoped<IRouteRegistrationService, RouteRegistrationService>();
        services.AddScoped<IDefaultContentSeeder, DefaultContentSeeder>();
        services.AddScoped<CMSRouteTransformer>();
    }

    private static void AddDomainModels(IServiceCollection services)
    {
        services.AddScoped<ContentBlockModel>();
        services.AddScoped<IContentBlockModel>(sp => sp.GetRequiredService<ContentBlockModel>());

        services.AddScoped<PageModel>();
        services.AddScoped<IPageModel>(sp => sp.GetRequiredService<PageModel>());

        services.AddScoped<ArticleListModel>();
        services.AddScoped<IArticleListModel>(sp => sp.GetRequiredService<ArticleListModel>());

        services.AddScoped<ContentZoneModel>();
        services.AddScoped<IContentZoneModel>(sp => sp.GetRequiredService<ContentZoneModel>());

        services.AddScoped<WidgetRegistrationModel>();
        services.AddScoped<PageControllerRegistrationModel>();
        services.AddScoped<CMSRouteModel>();
        services.AddScoped<FormComponentRegistrationModel>();

        services.AddScoped<ArticleViewComponent>();
        services.AddScoped<IRoutableViewComponent>(sp => sp.GetRequiredService<ArticleViewComponent>());

        services.AddScoped<IArticleModel, ArticleModel>();

        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile(new MappingProfile()));
        services.AddSingleton<IMapper>(mapperConfig.CreateMapper());
    }

    private static void AddMvcApplicationParts(IServiceCollection services)
    {
        services.Configure<MvcOptions>(_ => { });
        services.AddControllersWithViews().ConfigureApplicationPartManager(apm =>
        {
            var coreAsm = typeof(GenericPageController).Assembly;
            if (!apm.ApplicationParts.Any(p => p.Name == coreAsm.GetName().Name))
                apm.ApplicationParts.Add(new AssemblyPart(coreAsm));

            var formsAsm = typeof(FormFieldsTagHelper).Assembly;
            if (!apm.ApplicationParts.Any(p => p.Name == formsAsm.GetName().Name))
                apm.ApplicationParts.Add(new AssemblyPart(formsAsm));

            var presentationAsm = typeof(ContentZoneViewComponent).Assembly;
            if (!apm.ApplicationParts.Any(p => p.Name == presentationAsm.GetName().Name))
            {
                apm.ApplicationParts.Add(new AssemblyPart(presentationAsm));
                apm.ApplicationParts.Add(new CompiledRazorAssemblyPart(presentationAsm));
            }
        });
    }
}
