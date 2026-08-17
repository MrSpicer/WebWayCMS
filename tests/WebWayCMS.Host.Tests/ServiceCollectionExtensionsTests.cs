using System.Security.Claims;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Identity;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Article;
using WebWayCMS.Models.ContentBlock;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Models.Page;
using WebWayCMS.Pages;
using WebWayCMS.Routing;
using WebWayCMS.Startup;

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
            Assert.That(builder.Services.Any(d => d.ServiceType == typeof(IContentStore<PageDTO>)), Is.True);
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

        Assert.That(builder.Services.Any(d => d.ServiceType == typeof(WebWayCMS.Data.DbContexts.CmsDbContext)), Is.True);
    }

    [Test]
    public void AddWebWayCmsAdmin_TwoArg_RegistersAdminHandlerRegistry()
    {
        var builder = NewBuilder();
        AddConnection(builder);

        builder.Services.AddWebWayCmsAdmin(builder.Configuration);

        Assert.That(builder.Services.Any(d => d.ServiceType == typeof(IAdminHandlerRegistry)), Is.True);
    }

    [Test]
    public void AddWebWayCms_WithBuilderCallback_WiresHostExtensions()
    {
        var builder = NewBuilder();
        AddConnection(builder);
        var assembly = typeof(HostDto).Assembly;
        var profile = new HostProfile();

        builder.Services.AddWebWayCms(builder.Configuration, cms =>
        {
            cms.AddApplicationAssembly(assembly);
            cms.AddContentType<HostDto>("hostdtos");
            cms.AddMappingProfile(profile);
            cms.AddMigrationsContext<HostMigrationsContext>("__EFMigrationsHistory_Host");
        });

        using var app = builder.Build();
        using var scope = app.Services.CreateScope();

        var mapper = app.Services.GetRequiredService<IMapper>();
        var assemblyCatalog = app.Services.GetRequiredService<CmsAssemblyCatalog>();
        var migrationsCatalog = app.Services.GetRequiredService<CmsMigrationsContextCatalog>();
        var cmsDbContext = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        Assert.Multiple(() =>
        {
            Assert.That(scope.ServiceProvider.GetService<IContentStore<HostDto>>(), Is.Not.Null);
            Assert.That(mapper.Map<HostDto, HostViewModel>(new HostDto { Value = 42 }).Value, Is.EqualTo(42));
            Assert.That(assemblyCatalog.Assemblies, Does.Contain(assembly));
            Assert.That(migrationsCatalog.Contexts, Does.Contain(typeof(HostMigrationsContext)));
            // Proves DI actually injects IEnumerable<ICmsModelExtension> into CmsDbContext (the
            // host's entity configuration reached the model through the container, not by hand).
            Assert.That(cmsDbContext.Model.FindEntityType(typeof(HostDto)), Is.Not.Null);
        });
    }

    [Test]
    public void AddWebWayCmsRendering_WithBuilderCallback_RegistersHostContentStoreWithoutAdmin()
    {
        var builder = NewBuilder();
        AddConnection(builder);

        builder.Services.AddWebWayCmsRendering(builder.Configuration, cms =>
        {
            cms.AddContentType<HostDto>("hostdtos");
        });

        using var app = builder.Build();
        using var scope = app.Services.CreateScope();

        Assert.Multiple(() =>
        {
            Assert.That(scope.ServiceProvider.GetService<IContentStore<HostDto>>(), Is.Not.Null);
            Assert.That(app.Services.GetService<IAdminHandlerRegistry>(), Is.Null);
        });
    }

    private sealed class HostDto : IVersionedContent
    {
        public Guid VersionId { get; set; }
        public ContentVersion Version { get; set; } = new();
        public int Value { get; set; }
    }

    private sealed class HostDtoEntityConfiguration : IEntityTypeConfiguration<HostDto>
    {
        public void Configure(EntityTypeBuilder<HostDto> entity)
        {
            entity.ConfigureContentLink();
            entity.ToTable("HostDtos");
        }
    }

    private sealed class HostViewModel
    {
        public int Value { get; set; }
    }

    private sealed class HostProfile : Profile
    {
        public HostProfile()
        {
            CreateMap<HostDto, HostViewModel>(s => new HostViewModel { Value = s.Value });
        }
    }

    private sealed class HostMigrationsContext : CmsExtensionDbContext<HostMigrationsContext>
    {
        public HostMigrationsContext(
            DbContextOptions<HostMigrationsContext> options,
            IEnumerable<ICmsModelExtension> modelExtensions)
            : base(options, modelExtensions)
        {
        }
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
            Assert.That(app.Services.GetService<IWidgetRegistry>(), Is.Not.Null);
            Assert.That(app.Services.GetService<IPageControllerRegistry>(), Is.Not.Null);
            Assert.That(app.Services.GetService<IMapper>(), Is.Not.Null);
            // Configure<T> lambdas execute on options resolution.
            Assert.That(app.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value.ForwardedHeaders, Is.Not.EqualTo(ForwardedHeaders.None));
            Assert.That(app.Services.GetRequiredService<IOptions<RouteOptions>>().Value.ConstraintMap.ContainsKey("notreserved"), Is.True);
            Assert.That(app.Services.GetRequiredService<IOptions<IdentityOptions>>().Value.Password.RequiredLength, Is.EqualTo(12));

            var identityOptions = app.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;
            Assert.That(identityOptions.Lockout.MaxFailedAccessAttempts, Is.EqualTo(5));
            Assert.That(identityOptions.Lockout.DefaultLockoutTimeSpan, Is.EqualTo(TimeSpan.FromMinutes(15)));
            Assert.That(identityOptions.Lockout.AllowedForNewUsers, Is.True);

            var appCookie = app.Services
                .GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>()
                .Get(IdentityConstants.ApplicationScheme);
            Assert.That(appCookie.Cookie.HttpOnly, Is.True);
            Assert.That(appCookie.Cookie.SecurePolicy, Is.EqualTo(CookieSecurePolicy.Always));
            Assert.That(appCookie.Cookie.SameSite, Is.EqualTo(SameSiteMode.Lax));
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
            Assert.That(p.GetService<IContentStore<ArticleDTO>>(), Is.Not.Null);
            Assert.That(p.GetService<IContentStore<ArticleListDTO>>(), Is.Not.Null);
            Assert.That(p.GetService<IContentStore<ContentBlockDTO>>(), Is.Not.Null);
            Assert.That(p.GetService<IContentBlockModel>(), Is.Not.Null);
            Assert.That(p.GetService<IPageModel>(), Is.Not.Null);
            Assert.That(p.GetService<IArticleListModel>(), Is.Not.Null);
            Assert.That(p.GetService<IContentZoneModel>(), Is.Not.Null);
            Assert.That(p.GetService<IArticleModel>(), Is.Not.Null);
            Assert.That(p.GetService<CMSRouteTransformer>(), Is.Not.Null);
            Assert.That(p.GetService<IContentReadContext>(), Is.Not.Null);
            Assert.That(p.GetService<IChangeSetScope>(), Is.Not.Null);
            Assert.That(p.GetService<IContentUserContext>(), Is.Not.Null);
            Assert.That(p.GetService<IContentZoneService>(), Is.Not.Null);
            Assert.That(p.GetServices<IAdminCrudHandler>().Count(), Is.EqualTo(8));
        });
    }

    [Test]
    public void Provider_Rendering_ResolvesPublishedReadContext()
    {
        var builder = NewBuilder();
        AddConnection(builder);
        builder.Services.AddWebWayCmsRendering(builder.Configuration);
        using var app = builder.Build();
        using var scope = app.Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<IContentReadContext>();

        Assert.That(context, Is.InstanceOf<PublishedContentReadContext>());
        Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Published));
    }

    [Test]
    public void Provider_Admin_ResolvesPreviewAwareReadContext()
    {
        var builder = NewBuilder();
        AddConnection(builder);
        builder.Services.AddWebWayCms(builder.Configuration);
        using var app = builder.Build();
        using var scope = app.Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<IContentReadContext>();

        Assert.That(context, Is.InstanceOf<PreviewAwareReadContext>());
    }

    [Test]
    public void Provider_NoSmtp_ResolvesLoggingEmailSender()
    {
        var builder = NewBuilder();
        AddConnection(builder);
        builder.Services.AddWebWayCms(builder.Configuration);
        using var app = builder.Build();

        Assert.That(app.Services.GetRequiredService<IEmailSender>(), Is.InstanceOf<LoggingEmailSender>());
    }

    [Test]
    public void Provider_WithSmtp_ResolvesSmtpEmailSender()
    {
        var builder = NewBuilder();
        AddConnection(builder);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.example.com",
            ["Smtp:FromAddress"] = "noreply@example.com",
        });
        builder.Services.AddWebWayCms(builder.Configuration);
        using var app = builder.Build();

        Assert.That(app.Services.GetRequiredService<IEmailSender>(), Is.InstanceOf<SmtpEmailSender>());
    }

    [Test]
    public void Provider_HostEmailSender_Wins()
    {
        var builder = NewBuilder();
        AddConnection(builder);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.example.com",
            ["Smtp:FromAddress"] = "noreply@example.com",
        });
        builder.Services.AddSingleton<IEmailSender, HostEmailSender>();
        builder.Services.AddWebWayCms(builder.Configuration);
        using var app = builder.Build();

        Assert.That(app.Services.GetRequiredService<IEmailSender>(), Is.InstanceOf<HostEmailSender>());
    }

    private sealed class HostEmailSender : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage) => Task.CompletedTask;
    }
}

[TestFixture]
public class ContentReadContextTests
{
    private static ClaimsPrincipal Principal(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "TestAuth"));

    private static IHttpContextAccessor Accessor(HttpContext? context) =>
        new HttpContextAccessor { HttpContext = context };

    private static HttpContext Context(ClaimsPrincipal? user = null, string? previewCookie = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = user ?? new ClaimsPrincipal(new ClaimsIdentity());
        ctx.Request.Cookies = previewCookie == null
            ? Substitute.For<IRequestCookieCollection>()
            : Cookies(new Dictionary<string, string> { [PreviewConstants.CookieName] = previewCookie });
        return ctx;
    }

    private static IRequestCookieCollection Cookies(IReadOnlyDictionary<string, string> pairs)
    {
        var cookies = Substitute.For<IRequestCookieCollection>();
        foreach (var pair in pairs)
        {
            string? value;
            cookies.TryGetValue(pair.Key, out value).Returns(callInfo =>
            {
                callInfo[1] = pair.Value;
                return true;
            });
        }

        return cookies;
    }

    [Test]
    public void PublishedContentReadContext_IsAlwaysPublished()
    {
        var context = new PublishedContentReadContext();

        Assert.Multiple(() =>
        {
            Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Published));
            Assert.That(context.Culture, Is.Empty);
            Assert.That(context.Segment, Is.Empty);
        });
    }

    [Test]
    public void PreviewAwareReadContext_NullAccessor_Throws()
    {
        Assert.That(() => new PreviewAwareReadContext(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void PreviewAwareReadContext_CultureAndSegment_AreEmpty()
    {
        var context = new PreviewAwareReadContext(Accessor(Context()));

        Assert.Multiple(() =>
        {
            Assert.That(context.Culture, Is.Empty);
            Assert.That(context.Segment, Is.Empty);
        });
    }

    [Test]
    public void PreviewAwareReadContext_NoHttpContext_ReturnsPublished()
    {
        var context = new PreviewAwareReadContext(Accessor(null));

        Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Published));
    }

    [Test]
    public void PreviewAwareReadContext_Unauthenticated_ReturnsPublished()
    {
        var context = new PreviewAwareReadContext(Accessor(Context()));

        Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Published));
    }

    [Test]
    public void PreviewAwareReadContext_NullUser_ReturnsPublished()
    {
        var httpContext = Substitute.For<HttpContext>();
        httpContext.User.Returns((ClaimsPrincipal)null!);
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var context = new PreviewAwareReadContext(accessor);

        Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Published));
    }

    [Test]
    public void PreviewAwareReadContext_NullIdentity_ReturnsPublished()
    {
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal() };
        var context = new PreviewAwareReadContext(Accessor(ctx));

        Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Published));
    }

    [Test]
    public void PreviewAwareReadContext_AuthenticatedNonEditor_ReturnsPublished()
    {
        var context = new PreviewAwareReadContext(Accessor(Context(Principal("Guest"))));

        Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Published));
    }

    [Test]
    public void PreviewAwareReadContext_AdminWithoutCookie_ReturnsPublished()
    {
        var context = new PreviewAwareReadContext(Accessor(Context(Principal("Admin"))));

        Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Published));
    }

    [Test]
    public void PreviewAwareReadContext_AdminWithWrongCookie_ReturnsPublished()
    {
        var context = new PreviewAwareReadContext(Accessor(Context(Principal("Admin"), previewCookie: "0")));

        Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Published));
    }

    [Test]
    public void PreviewAwareReadContext_AdminWithValidCookie_ReturnsDraft()
    {
        var context = new PreviewAwareReadContext(Accessor(Context(Principal("Admin"), previewCookie: PreviewConstants.CookieValue)));

        Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Draft));
    }

    [Test]
    public void PreviewAwareReadContext_EditorWithValidCookie_ReturnsDraft()
    {
        var context = new PreviewAwareReadContext(Accessor(Context(Principal("Editor"), previewCookie: PreviewConstants.CookieValue)));

        Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Draft));
    }

    [Test]
    public void HttpContentUserContext_NullAccessor_Throws()
    {
        Assert.That(() => new HttpContentUserContext(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void HttpContentUserContext_NoHttpContext_ReturnsNull()
    {
        var context = new HttpContentUserContext(Accessor(null));

        Assert.That(context.CurrentUserId, Is.Null);
    }

    [Test]
    public void HttpContentUserContext_NoNameIdentifierClaim_ReturnsNull()
    {
        var context = new HttpContentUserContext(Accessor(Context(Principal("Admin"))));

        Assert.That(context.CurrentUserId, Is.Null);
    }

    [Test]
    public void HttpContentUserContext_NullUser_ReturnsNull()
    {
        var httpContext = Substitute.For<HttpContext>();
        httpContext.User.Returns((ClaimsPrincipal)null!);
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var context = new HttpContentUserContext(accessor);

        Assert.That(context.CurrentUserId, Is.Null);
    }

    [Test]
    public void HttpContentUserContext_InvalidGuidClaim_ReturnsNull()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "not-a-guid") }, "TestAuth"));
        var context = new HttpContentUserContext(Accessor(Context(user)));

        Assert.That(context.CurrentUserId, Is.Null);
    }

    [Test]
    public void HttpContentUserContext_ValidGuidClaim_ReturnsUserId()
    {
        var expected = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, expected.ToString()) }, "TestAuth"));
        var context = new HttpContentUserContext(Accessor(Context(user)));

        Assert.That(context.CurrentUserId, Is.EqualTo(expected));
    }
}