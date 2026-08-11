using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

using WebWayCMS.Data.Services;
using WebWayCMS.Services;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsDefaultPageSeeder
{
    internal static WebApplication EnsureDefaultHomePage(this WebApplication app, bool seedAdminPage, bool throwOnError = false)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = Log.ForContext(typeof(CmsDefaultPageSeeder));

        try
        {
            var seeder = services.GetRequiredService<IDefaultContentSeeder>();
            seeder.SeedDefaultPagesAsync(seedAdminPage).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error occurred creating default home page.");
            if (throwOnError)
            {
                throw;
            }
        }

        return app;
    }
}
