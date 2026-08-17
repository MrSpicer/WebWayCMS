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
    internal static void AddRenderingCoreTypes(
        IServiceCollection services,
        IReadOnlyList<Profile>? hostProfiles = null,
        IReadOnlyList<System.Reflection.Assembly>? hostAssemblies = null)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<UserService>();

        services.AddScoped<IViewDiscoveryService, ViewDiscoveryService>();

        services.AddSingleton<IWidgetRegistry, WidgetRegistry>();
        services.AddSingleton<IFormComponentRegistry, FormComponentRegistry>();
        services.AddScoped<IFormComponentResolver, FormComponentResolver>();

        AddContentServices(services);
        AddRoutingServices(services);
        AddDomainModels(services, hostProfiles);
        AddMvcApplicationParts(services, hostAssemblies);
    }

    private static void AddContentServices(IServiceCollection services)
    {
        services.AddScoped<IContentReadContext, PublishedContentReadContext>();
        services.AddScoped<IContentUserContext, HttpContentUserContext>();
        services.AddScoped<IChangeSetScope, ChangeSetScope>();

        AddContentStore<PageDTO>(services, "pages");
        AddContentStore<ArticleDTO>(services, "articles");
        AddContentStore<ArticleListDTO>(services, "articlelists");
        AddContentStore<ContentBlockDTO>(services, "contentblocks");
        AddContentStore<ContentZoneDTO>(services, "contentzones");
        AddContentStore<ContentZoneItemDTO>(services, "contentzoneitems");
        AddContentStore<WidgetRegistrationDTO>(services, "widgets");
        AddContentStore<PageControllerRegistrationDTO>(services, "pagetypes");
        AddContentStore<FormComponentRegistrationDTO>(services, "formcomponents");

        services.AddScoped<IContentZoneService, ContentZoneService>();
        services.AddScoped<IWidgetRegistrationService, WidgetRegistrationService>();
        services.AddScoped<IPageControllerRegistrationService, PageControllerRegistrationService>();
        services.AddScoped<IFormComponentRegistrationService, FormComponentRegistrationService>();
    }

    internal static void AddContentStore<T>(IServiceCollection services, string contentTypeKey)
        where T : class, IVersionedContent
    {
        services.AddScoped<IContentStore<T>>(sp => new ContentStore<T>(
            sp.GetRequiredService<CmsDbContext>(),
            sp.GetRequiredService<IContentReadContext>(),
            sp.GetRequiredService<IChangeSetScope>(),
            sp.GetRequiredService<IContentUserContext>(),
            contentTypeKey));
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

    private static void AddDomainModels(IServiceCollection services, IReadOnlyList<Profile>? hostProfiles = null)
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

        var mapperConfig = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile(new MappingProfile());
            if (hostProfiles != null)
            {
                foreach (var profile in hostProfiles)
                    cfg.AddProfile(profile);
            }
        });
        services.AddSingleton<IMapper>(mapperConfig.CreateMapper());
    }

    private static void AddMvcApplicationParts(IServiceCollection services, IReadOnlyList<System.Reflection.Assembly>? hostAssemblies = null)
    {
        services.Configure<MvcOptions>(_ => { });
        services.AddControllersWithViews().ConfigureApplicationPartManager(apm =>
        {
            AddAssemblyPart(apm, typeof(GenericPageController).Assembly, includeCompiledRazor: false);

            AddAssemblyPart(apm, typeof(FormFieldsTagHelper).Assembly, includeCompiledRazor: false);

            AddAssemblyPart(apm, typeof(ContentZoneViewComponent).Assembly, includeCompiledRazor: true);

            if (hostAssemblies != null)
            {
                foreach (var assembly in hostAssemblies)
                    AddAssemblyPart(apm, assembly, includeCompiledRazor: true);
            }
        });
    }

    private static void AddAssemblyPart(ApplicationPartManager apm, System.Reflection.Assembly assembly, bool includeCompiledRazor)
    {
        if (!apm.ApplicationParts.Any(p => p.Name == assembly.GetName().Name))
        {
            apm.ApplicationParts.Add(new AssemblyPart(assembly));
            if (includeCompiledRazor)
                apm.ApplicationParts.Add(new CompiledRazorAssemblyPart(assembly));
        }
    }
}
