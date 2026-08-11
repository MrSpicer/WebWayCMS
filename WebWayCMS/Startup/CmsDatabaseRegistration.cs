using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Data.DbContexts;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsDatabaseRegistration
{
    internal static void ConfigureDatabaseServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.AddDbContext<CmsDbContext>(options =>
            options.UseNpgsql(connectionString, b => b.MigrationsHistoryTable("__EFMigrationsHistory")));

#if DEBUG
        services.AddDatabaseDeveloperPageExceptionFilter();
#endif
    }
}
