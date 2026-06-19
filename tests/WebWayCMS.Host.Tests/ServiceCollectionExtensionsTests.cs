using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Article;
using WebWayCMS.Models.ContentBlock;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Models.Page;
using WebWayCMS.Pages;
using WebWayCMS.Routing;

namespace WebWayCMS.Host.Tests;

[TestFixture]
public class ServiceCollectionExtensionsTests
{
	// AddDefaultUI (invoked by AddWebWayCms) resolves the host's entry assembly, which only works
	// inside a WebApplicationBuilder context — not on a bare ServiceCollection under the test host.
	private static WebApplicationBuilder NewBuilder() => WebApplication.CreateBuilder();

	private static void AddConnection(WebApplicationBuilder builder) =>
		builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
		{
			["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test;Username=u;Password=p"
		});

	[Test]
	public void AddWebWayCms_NoConfig_RegistersCoreServices()
	{
		var builder = NewBuilder();

		var result = builder.Services.AddWebWayCms();

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.SameAs(builder.Services));
			Assert.That(builder.Services.Any(d => d.ServiceType == typeof(IPageService)), Is.True);
			Assert.That(builder.Services.Any(d => d.ServiceType == typeof(IMapper)), Is.True);
			Assert.That(builder.Services.Any(d => d.ServiceType == typeof(IAdminHandlerRegistry)), Is.True);
		});
	}

	[Test]
	public void AddWebWayCms_WithConfig_RegistersDbContexts()
	{
		var builder = NewBuilder();
		AddConnection(builder);

		builder.Services.AddWebWayCms(builder.Configuration);

		Assert.That(builder.Services.Any(d => d.ServiceType == typeof(WebWayCMS.Data.DbContexts.PageContext)), Is.True);
	}

	[Test]
	public void AddWebWayCms_MissingConnectionString_Throws()
	{
		var builder = NewBuilder();

		Assert.That(() => builder.Services.AddWebWayCms(builder.Configuration), Throws.InvalidOperationException);
	}

	[Test]
	public void Provider_ResolvesRegistriesAndOptionsFactories()
	{
		var builder = NewBuilder();
		AddConnection(builder);
		builder.Services.AddWebWayCms(builder.Configuration);
		using var app = builder.Build();

		Assert.Multiple(() =>
		{
			Assert.That(app.Services.GetService<IContentZoneComponentRegistry>(), Is.Not.Null);
			Assert.That(app.Services.GetService<IPageControllerRegistry>(), Is.Not.Null);
			Assert.That(app.Services.GetService<IMapper>(), Is.Not.Null);
			// Configure<T> lambdas execute on options resolution.
			Assert.That(app.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value.ForwardedHeaders, Is.Not.EqualTo(ForwardedHeaders.None));
			Assert.That(app.Services.GetRequiredService<IOptions<RouteOptions>>().Value.ConstraintMap.ContainsKey("notreserved"), Is.True);
			Assert.That(app.Services.GetRequiredService<IOptions<IdentityOptions>>().Value.Password.RequiredLength, Is.EqualTo(12));
		});
	}

	[Test]
	public void Provider_ResolvesContentServicesAndModels()
	{
		var builder = NewBuilder();
		AddConnection(builder);
		builder.Services.AddWebWayCms(builder.Configuration);
		using var app = builder.Build();
		using var scope = app.Services.CreateScope();
		var p = scope.ServiceProvider;

		Assert.Multiple(() =>
		{
			Assert.That(p.GetService<IContentService<ArticleDTO>>(), Is.Not.Null);
			Assert.That(p.GetService<IContentService<ArticleListDTO>>(), Is.Not.Null);
			Assert.That(p.GetService<IContentService<ContentBlockDTO>>(), Is.Not.Null);
			Assert.That(p.GetService<IContentBlockModel>(), Is.Not.Null);
			Assert.That(p.GetService<IPageModel>(), Is.Not.Null);
			Assert.That(p.GetService<IArticleListModel>(), Is.Not.Null);
			Assert.That(p.GetService<IContentZoneModel>(), Is.Not.Null);
			Assert.That(p.GetService<IArticleModel>(), Is.Not.Null);
			Assert.That(p.GetService<PageRouteTransformer>(), Is.Not.Null);
			Assert.That(p.GetServices<IAdminCrudHandler>().Count(), Is.EqualTo(4));
			// Blazor SSR rendering services (and the widget-registry factory lambda).
			Assert.That(p.GetService<WebWayCMS.Rendering.ICmsPageRenderer>(), Is.Not.Null);
			Assert.That(p.GetService<WebWayCMS.Presentation.Rendering.IContentZoneResolver>(), Is.Not.Null);
			Assert.That(p.GetService<WebWayCMS.Presentation.Rendering.IContentZoneWidgetRegistry>(), Is.Not.Null);
		});
	}
}
