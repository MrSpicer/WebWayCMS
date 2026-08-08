using System.Reflection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Serilog;
using Serilog.Events;

using WebWayCMS.ContentZones;
using WebWayCMS.Controllers;
using WebWayCMS.Controllers.Admin;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data;
using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Mcp;
using WebWayCMS.Models.Article;
using WebWayCMS.Models.ContentBlock;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Models.Page;
using WebWayCMS.Models.WidgetRegistration;
using WebWayCMS.Pages;
using WebWayCMS.Routing;
using WebWayCMS.TagHelpers;
using WebWayCMS.ViewComponents;

namespace WebWayCMS;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CMS rendering services (data, models, routing, content zones, Identity)
    /// and adds MVC application parts for public-facing controllers, ViewComponents, and tag helpers.
    /// Does NOT register admin controllers, admin views, admin handlers, or MCP.
    /// </summary>
    public static IServiceCollection AddWebWayCmsRendering(this IServiceCollection services, IConfiguration configuration)
    {
        ConfigureDatabaseServices(services, configuration);
        ConfigureForwardedHeaders(services);
        MapRenderingTypes(services);
        ConfigureAuthorization(services);
        ConfigureRateLimiting(services);
        services.Configure<CspOptions>(configuration.GetSection(CspOptions.SectionName));
        return services;
    }

    /// <summary>
    /// Registers the full CMS including admin surface (controllers, views, handlers, MCP).
    /// Calls <see cref="AddWebWayCmsRendering"/> internally, then layers on admin-only registrations.
    /// </summary>
    public static IServiceCollection AddWebWayCmsAdmin(this IServiceCollection services, IConfiguration configuration)
    {
        AddWebWayCmsRendering(services, configuration);
        MapAdminTypes(services);
        services.AddWebWayCmsMcp(configuration);
        return services;
    }

    /// <summary>
    /// Backwards-compatible overload that assumes database contexts already configured by host.
    /// Registers the full CMS with admin surface.
    /// </summary>
    public static IServiceCollection AddWebWayCms(this IServiceCollection services)
    {
        ConfigureForwardedHeaders(services);
        AddRenderingCoreTypes(services);
        ConfigureAuthorization(services);
        ConfigureRateLimiting(services);
        MapAdminTypes(services);
        return services;
    }

    /// <summary>
    /// Backwards-compatible overload. Registers the full CMS with admin surface and EF Core.
    /// </summary>
    public static IServiceCollection AddWebWayCms(this IServiceCollection services, IConfiguration configuration)
    {
        return AddWebWayCmsAdmin(services, configuration);
    }

    private static void ConfigureRateLimiting(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(
                AuthRateLimiting.GetPartition);
        });
    }

    private static void ConfigureForwardedHeaders(IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });
    }

    private static void ConfigureDatabaseServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.AddDbContext<CmsDbContext>(options =>
            options.UseNpgsql(connectionString, b => b.MigrationsHistoryTable("__EFMigrationsHistory")));

#if DEBUG
        services.AddDatabaseDeveloperPageExceptionFilter();
#endif
    }

    private static void AddRenderingCoreTypes(IServiceCollection services)
    {
#if DEBUG
        services.AddSingleton<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, WebWayCMS.Services.DevEmailSender>();
#endif
        services.AddHttpContextAccessor();
        services.AddSingleton<WebWayCMS.Services.UserService>();

        services.AddScoped<WebWayCMS.Services.IViewDiscoveryService, WebWayCMS.Services.ViewDiscoveryService>();

        services.AddSingleton<IWidgetRegistry, WidgetRegistry>();

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

        services.AddScoped<IContentService<WidgetRegistrationDTO>>(sp =>
        {
            var ctx = sp.GetRequiredService<CmsDbContext>();
            return new ContentService<WidgetRegistrationDTO>(ctx);
        });

        services.AddSingleton<IPageControllerRegistry>(sp =>
        {
            var assemblies = new[]
            {
                typeof(GenericPageController).Assembly,
                Assembly.GetEntryAssembly()
            }.Where(a => a != null).Distinct().Cast<Assembly>();
            return new PageControllerRegistry(assemblies);
        });

        services.AddScoped<PageRouteTransformer>();

        services.AddScoped<ContentBlockModel>();
        services.AddScoped<IContentBlockModel>(sp => sp.GetRequiredService<ContentBlockModel>());

        services.AddScoped<PageModel>();
        services.AddScoped<IPageModel>(sp => sp.GetRequiredService<PageModel>());

        services.AddScoped<ArticleListModel>();
        services.AddScoped<IArticleListModel>(sp => sp.GetRequiredService<ArticleListModel>());

        services.AddScoped<ContentZoneModel>();
        services.AddScoped<IContentZoneModel>(sp => sp.GetRequiredService<ContentZoneModel>());

        services.AddScoped<WidgetRegistrationModel>();

        services.AddScoped<IArticleModel, ArticleModel>();

        services.AddScoped<ISubRouteContent, ArticleSubRouteResolver>();

        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile(new MappingProfile()));
        services.AddSingleton<IMapper>(mapperConfig.CreateMapper());

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

    private static void MapRenderingTypes(IServiceCollection services)
    {
        AddRenderingCoreTypes(services);
    }

    private static void MapAdminTypes(IServiceCollection services)
    {
        services.Configure<RouteOptions>(o => o.ConstraintMap["notreserved"] = typeof(NotReservedConstraint));
        services.AddScoped<IAdminHandlerRegistry, AdminHandlerRegistry>();

        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<ContentBlockModel>());
        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<PageModel>());
        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<ArticleListModel>());
        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<ContentZoneModel>());
        services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<WidgetRegistrationModel>());

        services.AddSingleton<IPageControllerRegistry>(sp =>
        {
            var assemblies = new[]
            {
                typeof(GenericPageController).Assembly,
                typeof(AdminContentController).Assembly,
                Assembly.GetEntryAssembly()
            }.Where(a => a != null).Distinct().Cast<Assembly>();
            return new PageControllerRegistry(assemblies);
        });

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

    private static void ConfigureAuthorization(IServiceCollection services)
    {
        services.AddDefaultIdentity<IdentityUser>(
                identityOptions =>
                {
                    identityOptions.SignIn.RequireConfirmedEmail = true;
                    identityOptions.Password.RequireDigit = true;
                    identityOptions.Password.RequireLowercase = true;
                    identityOptions.Password.RequireNonAlphanumeric = true;
                    identityOptions.Password.RequireUppercase = true;
                    identityOptions.Password.RequiredLength = 12;

                    identityOptions.Lockout.AllowedForNewUsers = true;
                    identityOptions.Lockout.MaxFailedAccessAttempts = 5;
                    identityOptions.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                }
                )
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<CmsDbContext>()
            .AddDefaultUI();

        services.ConfigureApplicationCookie(cookieOptions =>
        {
            cookieOptions.Cookie.HttpOnly = true;
            cookieOptions.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            cookieOptions.Cookie.SameSite = SameSiteMode.Strict;
        });
    }
}
