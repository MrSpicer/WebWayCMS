using Serilog;
using Serilog.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace WebWayCMS.Logging;

public static class SerilogExtensions
{
    public static IHostBuilder UseCmsSerilog(this IHostBuilder hostBuilder, IConfiguration configuration)
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
			loggerConfig.MinimumLevel.Override("Microsoft", LogEventLevel.Information);

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
}
