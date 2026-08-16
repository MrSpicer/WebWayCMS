using Microsoft.Extensions.Hosting;

using Serilog;
using Serilog.Events;

namespace WebWayCMS.Logging;

public static class SerilogExtensions
{
    public static IHostBuilder UseCmsSerilog(this IHostBuilder hostBuilder)
    {
        var runningInContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

        hostBuilder.UseSerilog((context, services, loggerConfig) =>
        {
            // Start from configuration (allows overriding in appsettings.json / env vars)
            loggerConfig
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext();

            // Provide reasonable defaults if not specified
            ApplyMinimumLevelOverrides(loggerConfig, context.HostingEnvironment.IsDevelopment());

            // Always log to console
            loggerConfig.WriteTo.Console();

            // Preserve local rolling file sink for developers
            if (!runningInContainer)
            {
                loggerConfig.WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day);
            }
        });

        return hostBuilder;
    }

    /// <summary>
    /// Applies the CMS's default minimum-level overrides. Outside Development, EF Core's per-query
    /// SQL statements are silenced (they dominate the one-line-per-request summary); local debugging
    /// keeps them.
    /// </summary>
    internal static void ApplyMinimumLevelOverrides(LoggerConfiguration loggerConfig, bool isDevelopment)
    {
        loggerConfig.MinimumLevel.Override("Microsoft", LogEventLevel.Information);

        // The request-logging middleware emits one summary line per request; silence ASP.NET Core's
        // own "Request starting"/"Request finished" pair so a single request isn't logged three times.
        loggerConfig.MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning);

        if (!isDevelopment)
        {
            loggerConfig.MinimumLevel.Override(
                "Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning);
        }
    }
}