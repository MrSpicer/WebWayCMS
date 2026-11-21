using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Services.Caching;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsDatabaseRegistration
{
    internal static void ConfigureDatabaseServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        // Two CmsDbContext instances with different extension sets in one process would otherwise
        // share a stale model — fold the extension types into the model cache key.
        // PendingModelChangesWarning is ignored because a host's ICmsModelExtension contributions are
        // part of the runtime model but are migrated by the host's own migrations-only context, not
        // by CmsDbContext — so the runtime model legitimately differs from the CMS migration history.
        // The interceptor flags public changes on every save and bumps the cross-host generation
        // stamp at commit (CmsCachingRegistration registers it before this point).
        services.AddDbContext<CmsDbContext>((sp, options) =>
            options.UseNpgsql(connectionString, b => b.MigrationsHistoryTable("__EFMigrationsHistory"))
                   .ReplaceService<IModelCacheKeyFactory, CmsModelCacheKeyFactory>()
                   .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                   .AddInterceptors(sp.GetRequiredService<PublicContentChangeInterceptor>()));

#if DEBUG
        services.AddDatabaseDeveloperPageExceptionFilter();
#endif
    }
}
