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
using WebWayCMS.Models.Image;
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
            Assert.That(p.GetService<IImageModel>(), Is.Not.Null);
            Assert.That(p.GetService<CMSRouteTransformer>(), Is.Not.Null);
            Assert.That(p.GetService<IContentReadContext>(), Is.Not.Null);
            Assert.That(p.GetService<IChangeSetScope>(), Is.Not.Null);
            Assert.That(p.GetService<IContentUserContext>(), Is.Not.Null);
            Assert.That(p.GetService<IContentZoneService>(), Is.Not.Null);
            Assert.That(p.GetServices<IAdminCrudHandler>().Count(), Is.EqualTo(13));
        });
    }

    [Test]
    public void Provider_BothModes_ResolveWidgetSlotDiscovery()
    {
        foreach (var add in new Action<IServiceCollection, IConfiguration>[]
                 {
                     (s, c) => s.AddWebWayCms(c),
                     (s, c) => s.AddWebWayCmsRendering(c)
                 })
        {
            var builder = NewBuilder();
            AddConnection(builder);
            add(builder.Services, builder.Configuration);
            using var app = builder.Build();
            using var scope = app.Services.CreateScope();

            Assert.That(scope.ServiceProvider.GetService<WebWayCMS.Services.IWidgetSlotDiscovery>(),
                Is.InstanceOf<WebWayCMS.ViewComponents.SlotDiscovery.ViewComponentSlotDiscovery>());
        }
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
    public void Provider_BothModes_ResolvePageHistoryService_AndAdminResolvesPageHistoryModel()
    {
        var admin = NewBuilder();
        AddConnection(admin);
        admin.Services.AddWebWayCms(admin.Configuration);
        using var adminApp = admin.Build();
        using var adminScope = adminApp.Services.CreateScope();

        var rendering = NewBuilder();
        AddConnection(rendering);
        rendering.Services.AddWebWayCmsRendering(rendering.Configuration);
        using var renderingApp = rendering.Build();
        using var renderingScope = renderingApp.Services.CreateScope();

        Assert.Multiple(() =>
        {
            Assert.That(adminScope.ServiceProvider.GetRequiredService<IPageHistoryService>(), Is.InstanceOf<PageHistoryService>());
            Assert.That(adminScope.ServiceProvider.GetRequiredService<WebWayCMS.Models.PageHistory.IPageHistoryModel>(), Is.Not.Null);
            Assert.That(renderingScope.ServiceProvider.GetRequiredService<IPageHistoryService>(), Is.InstanceOf<PageHistoryService>());
            Assert.That(renderingScope.ServiceProvider.GetService<WebWayCMS.Models.PageHistory.IPageHistoryModel>(), Is.Null);
        });
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

    [Test]
    public void Provider_NoRateLimitConfig_UsesDefaultAuthRateLimit()
    {
        var builder = NewBuilder();
        AddConnection(builder);
        builder.Services.AddWebWayCms(builder.Configuration);
        using var app = builder.Build();

        var options = app.Services.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(options.PermitLimit, Is.EqualTo(5));
            Assert.That(options.WindowSeconds, Is.EqualTo(60));
        });
    }

    [Test]
    public void Provider_RateLimitConfig_BindsAuthRateLimitAndGlobalLimiter()
    {
        var builder = NewBuilder();
        AddConnection(builder);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AuthRateLimiting:PermitLimit"] = "100",
            ["AuthRateLimiting:WindowSeconds"] = "30",
        });
        builder.Services.AddWebWayCms(builder.Configuration);
        using var app = builder.Build();

        var options = app.Services.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value;
        var limiter = app.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(options.PermitLimit, Is.EqualTo(100));
            Assert.That(options.WindowSeconds, Is.EqualTo(30));
            Assert.That(limiter.RejectionStatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
            Assert.That(limiter.GlobalLimiter, Is.Not.Null);
        });
    }

    [Test]
    public void Provider_InvalidRateLimitConfig_FailsValidation()
    {
        var builder = NewBuilder();
        AddConnection(builder);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AuthRateLimiting:PermitLimit"] = "0",
        });
        builder.Services.AddWebWayCms(builder.Configuration);
        using var app = builder.Build();

        Assert.That(() => app.Services.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value,
            Throws.InstanceOf<OptionsValidationException>());
    }

    [Test]
    public void Provider_RegistersUserManagement()
    {
        var builder = NewBuilder();
        AddConnection(builder);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["UserManagement:InvitationLifetimeDays"] = "3",
            ["Identity:AllowRegistration"] = "false",
        });
        builder.Services.AddWebWayCms(builder.Configuration);
        using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        Assert.Multiple(() =>
        {
            Assert.That(services.GetRequiredService<SignInManager<IdentityUser>>(), Is.InstanceOf<WebWayCMS.Users.CmsSignInManager>());
            Assert.That(services.GetRequiredService<WebWayCMS.Data.Services.Users.IUserAdminService>(), Is.Not.Null);
            Assert.That(services.GetRequiredService<WebWayCMS.Users.IUserManagementModel>(), Is.Not.Null);
            Assert.That(services.GetRequiredService<WebWayCMS.Data.Services.IAdminRouteReservations>(),
                Is.InstanceOf<WebWayCMS.Controllers.Admin.Handlers.AdminContentRouteReservations>());
            Assert.That(services.GetRequiredService<IOptions<RouteOptions>>().Value.ConstraintMap.ContainsKey("admincontenttype"), Is.True);
            Assert.That(services.GetRequiredService<IOptions<WebWayCMS.Users.InvitationTokenProviderOptions>>().Value.TokenLifespan,
                Is.EqualTo(TimeSpan.FromDays(3)));
            Assert.That(services.GetRequiredService<IOptions<WebWayCMS.Identity.IdentityRegistrationOptions>>().Value.AllowRegistration, Is.False);
            Assert.That(services.GetRequiredService<IOptions<IdentityOptions>>().Value.Tokens.ProviderMap,
                Does.ContainKey(WebWayCMS.Users.InvitationTokenProvider.ProviderName));
            Assert.That(services.GetServices<WebWayCMS.Interfaces.IRoutableViewComponent>(),
                Has.Some.InstanceOf<WebWayCMS.ViewComponents.UsersViewComponent>());
        });
    }

    [Test]
    public void Provider_InvalidInvitationLifetime_FailsValidation()
    {
        var builder = NewBuilder();
        AddConnection(builder);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["UserManagement:InvitationLifetimeDays"] = "0",
        });
        builder.Services.AddWebWayCms(builder.Configuration);
        using var app = builder.Build();

        Assert.That(() => app.Services.GetRequiredService<IOptions<WebWayCMS.Users.UserManagementOptions>>().Value,
            Throws.InstanceOf<OptionsValidationException>());
    }

    private sealed class HostEmailSender : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage) => Task.CompletedTask;
    }
}
