using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

using WebWayCMS.Data.DbContexts;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsMigrationRunner
{
    internal static WebApplication ApplyCmsPendingMigrations(this WebApplication app, bool throwOnError = true)
    {
        if (CmsStartupHelpers.IsSkipped("WEBWAYCMS_SKIP_MIGRATIONS"))
        {
            Log.ForContext(typeof(CmsMigrationRunner)).Information("Skipping CMS migrations due to WEBWAYCMS_SKIP_MIGRATIONS=true");
            return app;
        }

        var logger = Log.ForContext(typeof(CmsMigrationRunner));
        const int maxAttempts = 10;
        var delay = TimeSpan.FromSeconds(3);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = app.Services.CreateScope();
                var services = scope.ServiceProvider;
                Migrate(typeof(CmsDbContext), services, logger);

                // Host migrations run second, in registration order, so a host table's FK to the
                // CMS-owned ContentVersions table (already created above) is resolvable.
                var catalog = services.GetService<CmsMigrationsContextCatalog>();
                if (catalog != null)
                {
                    foreach (var contextType in catalog.Contexts)
                        Migrate(contextType, services, logger);
                }

                return app;
            }
            catch (Exception ex) when (IsTransientDbStartupException(ex) && attempt < maxAttempts)
            {
                logger.Warning("Database not yet available (attempt {Attempt}/{Max}). Retrying in {Delay}s...",
                    attempt, maxAttempts, delay.TotalSeconds);
                Thread.Sleep(delay);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
            catch (Exception ex)
            {
                logger.Error(ex, "An error occurred migrating CMS databases.");
                if (throwOnError) throw;
                return app;
            }
        }

        return app;
    }

    private static void Migrate(Type contextType, IServiceProvider services, ILogger logger)
    {
        var context = services.GetService(contextType) as DbContext;
        if (context == null)
        {
            logger.Warning("DbContext {Context} not registered; skipping migrations.", contextType.Name);
            return;
        }
        var pending = context.Database.GetPendingMigrations().ToList();
        if (pending.Count == 0)
        {
            logger.Debug("No pending migrations for {Context}", contextType.Name);
        }
        else
        {
            logger.Information("Applying {Count} migrations for {Context}: {Migrations}", pending.Count, contextType.Name, string.Join(", ", pending));
        }
        context.Database.Migrate();
    }

    internal static bool IsTransientDbStartupException(Exception ex)
    {
        var inner = ex.InnerException;
        while (inner != null)
        {
            if (inner is System.Net.Sockets.SocketException) return true;
            inner = inner.InnerException;
        }
        return false;
    }
}
