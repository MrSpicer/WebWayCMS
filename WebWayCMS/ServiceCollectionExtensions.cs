using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Mcp;
using WebWayCMS.Startup;

namespace WebWayCMS;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWebWayCmsRendering(this IServiceCollection services, IConfiguration configuration)
    {
        CmsDatabaseRegistration.ConfigureDatabaseServices(services, configuration);
        CmsHttpInfrastructureRegistration.ConfigureForwardedHeaders(services);
        CmsRenderingRegistration.AddRenderingCoreTypes(services);
        CmsIdentityRegistration.ConfigureAuthorization(services);
        CmsHttpInfrastructureRegistration.ConfigureRateLimiting(services);
        services.Configure<CspOptions>(configuration.GetSection(CspOptions.SectionName));
        return services;
    }

    public static IServiceCollection AddWebWayCmsAdmin(this IServiceCollection services, IConfiguration configuration)
    {
        AddWebWayCmsRendering(services, configuration);
        CmsAdminRegistration.MapAdminTypes(services);
        services.AddWebWayCmsMcp(configuration);
        return services;
    }

    public static IServiceCollection AddWebWayCms(this IServiceCollection services)
    {
        CmsHttpInfrastructureRegistration.ConfigureForwardedHeaders(services);
        CmsRenderingRegistration.AddRenderingCoreTypes(services);
        CmsIdentityRegistration.ConfigureAuthorization(services);
        CmsHttpInfrastructureRegistration.ConfigureRateLimiting(services);
        CmsAdminRegistration.MapAdminTypes(services);
        return services;
    }

    public static IServiceCollection AddWebWayCms(this IServiceCollection services, IConfiguration configuration)
    {
        return AddWebWayCmsAdmin(services, configuration);
    }
}
