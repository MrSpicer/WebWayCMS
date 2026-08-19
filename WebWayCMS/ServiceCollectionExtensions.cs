using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Mcp;
using WebWayCMS.Startup;

namespace WebWayCMS;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWebWayCmsRendering(this IServiceCollection services, IConfiguration configuration)
        => AddWebWayCmsRendering(services, configuration, null);

    public static IServiceCollection AddWebWayCmsRendering(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IWebWayCmsBuilder>? configure)
    {
        var builder = new WebWayCmsBuilder(services, configuration);

        CmsDatabaseRegistration.ConfigureDatabaseServices(services, configuration);
        configure?.Invoke(builder);
        builder.RegisterCatalogs();

        CmsHttpInfrastructureRegistration.ConfigureForwardedHeaders(services);
        CmsRenderingRegistration.AddRenderingCoreTypes(services, builder.Profiles, builder.Assemblies);
        CmsIdentityRegistration.ConfigureAuthorization(services, configuration);
        CmsHttpInfrastructureRegistration.ConfigureRateLimiting(services);
        services.Configure<CspOptions>(configuration.GetSection(CspOptions.SectionName));
        return services;
    }

    public static IServiceCollection AddWebWayCmsAdmin(this IServiceCollection services, IConfiguration configuration)
        => AddWebWayCmsAdmin(services, configuration, null);

    public static IServiceCollection AddWebWayCmsAdmin(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IWebWayCmsBuilder>? configure)
    {
        AddWebWayCmsRendering(services, configuration, configure);
        CmsAdminRegistration.MapAdminTypes(services, configuration);
        services.AddWebWayCmsMcp(configuration);
        return services;
    }

    /// <summary>
    /// Back-compatibility/test shim. Registers the rendering core types, Identity, forwarded headers,
    /// rate limiting, and the admin type map against an empty <see cref="IConfiguration"/> — it skips
    /// database and CSP registration and can never enable external-login providers, SMTP email, or
    /// <c>IdentityPasskeyOptions</c> (those are configuration-driven). Hosts should call
    /// <see cref="AddWebWayCms(IServiceCollection, IConfiguration)"/> (or the rendering/admin variants)
    /// instead.
    /// </summary>
    public static IServiceCollection AddWebWayCms(this IServiceCollection services)
    {
        CmsHttpInfrastructureRegistration.ConfigureForwardedHeaders(services);
        CmsRenderingRegistration.AddRenderingCoreTypes(services);
        CmsIdentityRegistration.ConfigureAuthorization(services, new ConfigurationBuilder().Build());
        CmsHttpInfrastructureRegistration.ConfigureRateLimiting(services);
        CmsAdminRegistration.MapAdminTypes(services, new ConfigurationBuilder().Build());
        return services;
    }

    public static IServiceCollection AddWebWayCms(this IServiceCollection services, IConfiguration configuration)
        => AddWebWayCms(services, configuration, null);

    public static IServiceCollection AddWebWayCms(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IWebWayCmsBuilder>? configure)
    {
        return AddWebWayCmsAdmin(services, configuration, configure);
    }
}
