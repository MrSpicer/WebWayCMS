using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using WebWayCMS.ContentZones;
using WebWayCMS.Controllers;        // GenericPageController
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.ContentBlock;
using WebWayCMS.Models.ContentZone;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System.Reflection;
using Microsoft.AspNetCore.Identity;
using WebWayCMS.Data.DbContexts;
using Serilog;
using Serilog.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using WebWayCMS.Models.Article;
using WebWayCMS.Models.Page;
using WebWayCMS.Pages;
using WebWayCMS.Presentation.Components.Account;
using WebWayCMS.Routing;

namespace WebWayCMS;

public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Registers CMS (Data, Models, Services) components and adds MVC application part for Controllers/ViewComponents.
	/// </summary>
	public static IServiceCollection AddWebWayCms(this IServiceCollection services)
	{
		// Backwards-compatible overload assumes database contexts already configured by host.
		AddCmsCore(services);
		return services;
	}

	/// <summary>
	/// Registers CMS services and configures EF Core DbContexts using the provided configuration.
	/// </summary>
	/// <param name="services">The DI service collection.</param>
	/// <param name="configuration">Application configuration for resolving connection string.</param>
	/// <returns>The same service collection for chaining.</returns>
	public static IServiceCollection AddWebWayCms(this IServiceCollection services, IConfiguration configuration)
	{
		ConfigureDatabaseServices(services, configuration);
		AddCmsCore(services);
		return services;
	}

	private static void AddCmsCore(IServiceCollection services)
	{
		ConfigureForwardedHeaders(services);
		MapTypes(services);
		ConfigureAuthorization(services);
	}

	private static void ConfigureForwardedHeaders(IServiceCollection services)
	{
		services.Configure<ForwardedHeadersOptions>(options =>
		{
			options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
			// Trust all upstream proxies within Docker's internal network
			options.KnownIPNetworks.Clear();
			options.KnownProxies.Clear();
		});
	}

	private static void ConfigureDatabaseServices(IServiceCollection services, IConfiguration configuration)
	{
		var connectionString = configuration.GetConnectionString("DefaultConnection")
			?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

		// Main application DB (Identity + app data)
		services.AddDbContext<ApplicationDbContext>(options =>
			options.UseNpgsql(connectionString, b => b.MigrationsHistoryTable("__EFMigrationsHistory_Application")));

		// Article DB/context can share the same connection or be configured separately in appsettings
		services.AddDbContext<ArticleContext>(options =>
			options.UseNpgsql(connectionString, b => b.MigrationsHistoryTable("__EFMigrationsHistory_Article")));

		// ContentBlock DB/context
		services.AddDbContext<ContentBlockContext>(options =>
			options.UseNpgsql(connectionString, b => b.MigrationsHistoryTable("__EFMigrationsHistory_ContentBlock")));

		// ContentZone DB/context
		services.AddDbContext<ContentZoneContext>(options =>
			options.UseNpgsql(connectionString, b => b.MigrationsHistoryTable("__EFMigrationsHistory_ContentZone")));

		// Page DB/context
		services.AddDbContext<PageContext>(options =>
			options.UseNpgsql(connectionString, b => b.MigrationsHistoryTable("__EFMigrationsHistory_Page")));

		#if DEBUG
		services.AddDatabaseDeveloperPageExceptionFilter();
		#endif
	}

	private static void MapTypes(IServiceCollection services)
	{
#if DEBUG
		services.AddSingleton<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, WebWayCMS.Services.DevEmailSender>();
#endif
		// Needed for UserService to inspect current HttpContext/User
		services.AddHttpContextAccessor();
		services.AddSingleton<WebWayCMS.Services.UserService>();

		// ViewComponent view discovery service
		services.AddScoped<WebWayCMS.Services.IViewDiscoveryService, WebWayCMS.Services.ViewDiscoveryService>();

		// Content Zone Component Registry - scans assemblies for registered ViewComponents
		services.AddSingleton<IContentZoneComponentRegistry>(sp =>
		{
			var assemblies = new[]
			{
				typeof(WebWayCMS.Presentation.Components.Widgets.ContentBlockWidget).Assembly,  // CMS.Presentation: [ContentZoneComponent] Blazor widgets
				Assembly.GetEntryAssembly()
			}.Where(a => a != null).Distinct().Cast<Assembly>();
			return new ContentZoneComponentRegistry(assemblies);
		});

		// Generic content service registrations to enable consumers to request IContentService<T>
		// Note: Each T must be bound to the correct DbContext through constructor injection of DbContext.
		services.AddScoped<IContentService<ArticleDTO>>(sp =>
		{
			var ctx = sp.GetRequiredService<ArticleContext>();
			return new ContentService<ArticleDTO>(ctx);
		});

		services.AddScoped<IContentService<ArticleListDTO>>(sp =>
		{
			var ctx = sp.GetRequiredService<ArticleContext>();
			return new ContentService<ArticleListDTO>(ctx);
		});


		services.AddScoped<IContentService<ContentBlockDTO>>(sp =>
		{
			var ctx = sp.GetRequiredService<ContentBlockContext>();
			return new ContentService<ContentBlockDTO>(ctx);
		});

		// ContentZone service registration
		services.AddScoped<IContentZoneService, ContentZoneService>();

		// Page service and model registrations
		services.AddScoped<IPageService, PageService>();

		// Page Controller Registry - scans assemblies for registered page controllers
		services.AddSingleton<IPageControllerRegistry>(sp =>
		{
			var assemblies = new[]
			{
				typeof(GenericPageController).Assembly,  // CMS.Core: [PageController] controllers
				Assembly.GetEntryAssembly()
			}.Where(a => a != null).Distinct().Cast<Assembly>();
			return new PageControllerRegistry(assemblies);
		});

		// PageRouteTransformer for dynamic page routing
		services.AddScoped<PageRouteTransformer>();

		// Register concrete model types once; expose via both their domain interface and IAdminCrudHandler
		// so all consumers share the same scoped instance.
		services.AddScoped<ContentBlockModel>();
		services.AddScoped<IContentBlockModel>(sp => sp.GetRequiredService<ContentBlockModel>());
		services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<ContentBlockModel>());

		services.AddScoped<PageModel>();
		services.AddScoped<IPageModel>(sp => sp.GetRequiredService<PageModel>());
		services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<PageModel>());

		services.AddScoped<ArticleListModel>();
		services.AddScoped<IArticleListModel>(sp => sp.GetRequiredService<ArticleListModel>());
		services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<ArticleListModel>());

		services.AddScoped<ContentZoneModel>();
		services.AddScoped<IContentZoneModel>(sp => sp.GetRequiredService<ContentZoneModel>());
		services.AddScoped<IAdminCrudHandler>(sp => sp.GetRequiredService<ContentZoneModel>());

		services.AddScoped<IArticleModel, ArticleModel>();

		// Dynamic page sub-route resolvers: components that can serve a path beneath a
		// matched page (e.g. an article slug). PageRouteTransformer consults these and
		// 404s sub-routes that no resolver can resolve.
		services.AddScoped<ISubRouteContent, ArticleSubRouteResolver>();

		// Admin CRUD handler registry
		services.Configure<RouteOptions>(o => o.ConstraintMap["notreserved"] = typeof(NotReservedConstraint));
		services.AddScoped<IAdminHandlerRegistry, AdminHandlerRegistry>();

		// Object mapper (in-house) configured from this assembly's profile
		var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile(new MappingProfile()));
		services.AddSingleton<IMapper>(mapperConfig.CreateMapper());

		// Register CMS.Core as an MVC application part so its page controllers are discovered (the
		// view layer is Blazor, so no Forms tag-helper / Presentation view parts are needed).
		// Sub-route validation is performed inside PageRouteTransformer (it only resolves a
		// page for a sub-route some ISubRouteContent resolver can serve), so no global filter
		// is needed here.
		services.Configure<MvcOptions>(_ => { }); // no-op to ensure MVC services available if host only calls minimal AddControllersWithViews later
		services.AddControllersWithViews().ConfigureApplicationPartManager(apm =>
		{
			var coreAsm = typeof(GenericPageController).Assembly; // CMS.Core (page controllers)
			if (!apm.ApplicationParts.Any(p => p.Name == coreAsm.GetName().Name))
				apm.ApplicationParts.Add(new AssemblyPart(coreAsm));
		});
		// Blazor SSR: register the Razor Components host alongside MVC so the CMS view layer
		// can render Razor components, with Interactive Server available for admin editing UI.
		// The matching endpoints + render mode are mapped in CMSExtensions.ConfigureMiddleware.
		services.AddRazorComponents()
			.AddInteractiveServerComponents();

		// Blazor SSR rendering services: the controller -> Blazor bridge, content-zone
		// resolution, and the component-name -> Razor-widget map that drives ContentZone's
		// DynamicComponent dispatch (populated as ViewComponents are migrated).
		services.AddScoped<WebWayCMS.Rendering.ICmsPageRenderer, WebWayCMS.Presentation.Rendering.CmsPageRenderer>();
		services.AddScoped<WebWayCMS.Presentation.Rendering.IContentZoneResolver, WebWayCMS.Presentation.Rendering.ContentZoneResolver>();
		services.AddScoped<WebWayCMS.Presentation.Rendering.IArticleWidgetResolver, WebWayCMS.Presentation.Rendering.ArticleWidgetResolver>();
		services.AddScoped<WebWayCMS.Presentation.Rendering.IFormOptionsProvider, WebWayCMS.Presentation.Rendering.FormOptionsProvider>();
		services.AddSingleton<WebWayCMS.Presentation.Rendering.IContentZoneWidgetRegistry>(_ =>
			new WebWayCMS.Presentation.Rendering.ContentZoneWidgetRegistry(
				new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
				{
					["ContentBlock"] = typeof(WebWayCMS.Presentation.Components.Widgets.ContentBlockWidget),
					["Layout"] = typeof(WebWayCMS.Presentation.Components.Widgets.LayoutWidget),
					["Page"] = typeof(WebWayCMS.Presentation.Components.Widgets.PageNavigationWidget),
					["Article"] = typeof(WebWayCMS.Presentation.Components.Widgets.ArticleWidget),
				}));
	}

	static void ConfigureAuthorization(IServiceCollection services)
	{
		// Identity and authentication
		services.AddDefaultIdentity<IdentityUser>(
				identityOptions =>
				{
					identityOptions.SignIn.RequireConfirmedEmail = true;
					identityOptions.Password.RequireDigit = true;
					identityOptions.Password.RequireLowercase = true;
					identityOptions.Password.RequireNonAlphanumeric = true;
					identityOptions.Password.RequireUppercase = true;
					identityOptions.Password.RequiredLength = 12;
				}
				)
			.AddRoles<IdentityRole>()
			.AddEntityFrameworkStores<ApplicationDbContext>();

		// The Blazor /Account/* components fully replace the scaffolded Identity UI, so point the
		// Identity application cookie at them (it previously defaulted to the now-removed
		// /Identity/Account/* Razor Pages).
		services.ConfigureApplicationCookie(options =>
		{
			options.LoginPath = "/Account/Login";
			options.LogoutPath = "/Account/Logout";
			options.AccessDeniedPath = "/Account/AccessDenied";
		});

		// Blazor Identity (account) components: cascading auth state, redirect/user-accessor helpers,
		// the revalidating server auth-state provider, and the typed email sender.
		services.AddCmsBlazorIdentity();
	}
}
