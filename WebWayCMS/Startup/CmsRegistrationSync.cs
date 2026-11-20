using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

using WebWayCMS.Data.Services.Registrations;

namespace WebWayCMS.Startup;

/// <summary>
/// The one boot step every registry seeder (widgets, page types, form components, content types) runs: build
/// the registrations the code declares, hand them to <see cref="RegistrationSynchronizer"/>, and log what it did.
/// A seeder only says what the code declares and which columns are code-owned.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class CmsRegistrationSync
{
    internal static WebApplication EnsureRegistrationsSynced<T>(
        this WebApplication app,
        string label,
        string skipEnvVar,
        bool throwOnError,
        Func<IServiceProvider, IRegistrationSyncTarget<T>> target,
        Func<IServiceProvider, ILogger, IEnumerable<RegistrationSyncItem<T>>> declared,
        Action<IServiceProvider, RegistrationSyncResult, ILogger>? afterSync = null)
    {
        var logger = Log.ForContext(typeof(CmsRegistrationSync));
        if (CmsStartupHelpers.IsSkipped(skipEnvVar))
        {
            logger.Information("Skipping {Label} registration seeding due to {EnvVar}=true", label, skipEnvVar);
            return app;
        }

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        try
        {
            var items = declared(services, logger).ToList();
            var result = RegistrationSynchronizer.SyncAsync(target(services), items).GetAwaiter().GetResult();

            foreach (var key in result.Inserted)
                logger.Information("Seeded {Label} registration '{Key}'", label, key);
            foreach (var key in result.Updated)
                logger.Information("Re-synced the code-owned fields of {Label} registration '{Key}'", label, key);
            foreach (var failure in result.Failed)
                logger.Warning("Failed to seed {Label} registration '{Key}': {Error}", label, failure.Key, failure.ErrorMessage);
            foreach (var key in result.Orphaned)
                logger.Warning("The {Label} registration '{Key}' has no matching code (left in place)", label, key);

            afterSync?.Invoke(services, result, logger);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error occurred seeding {Label} registrations.", label);
            if (throwOnError)
                throw;
        }

        return app;
    }

    /// <summary>
    /// The concrete classes of <paramref name="assemblies"/> carrying <typeparamref name="TAttribute"/>, with the
    /// attribute. An assembly that fails to load its types is logged and skipped.
    /// </summary>
    internal static IEnumerable<(Type Type, TAttribute Attribute)> TypesWith<TAttribute>(
        IEnumerable<Assembly> assemblies, Func<Type, bool> isCandidate, ILogger logger)
        where TAttribute : Attribute
    {
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to scan assembly {Assembly} for {Attribute}", assembly.FullName, typeof(TAttribute).Name);
                continue;
            }

            foreach (var type in types.Where(t => t.IsClass && !t.IsAbstract && isCandidate(t)))
            {
                var attribute = type.GetCustomAttribute<TAttribute>();
                if (attribute != null)
                    yield return (type, attribute);
            }
        }
    }
}
