using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

using WebWayCMS.Services.ContentSeeding;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsContentSeedRunner
{
    internal static WebApplication EnsureJsonContentSeeded(this WebApplication app, bool throwOnError = false)
    {
        if (CmsStartupHelpers.IsSkipped("WEBWAYCMS_SKIP_CONTENTSEED"))
        {
            Log.ForContext(typeof(CmsContentSeedRunner)).Information("Skipping JSON content seeding due to WEBWAYCMS_SKIP_CONTENTSEED=true");
            return app;
        }

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = Log.ForContext(typeof(CmsContentSeedRunner));

        try
        {
            var seeder = services.GetRequiredService<IJsonContentSeeder>();
            seeder.SeedAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error occurred seeding JSON content.");
            if (throwOnError)
            {
                throw;
            }
        }

        return app;
    }
}
