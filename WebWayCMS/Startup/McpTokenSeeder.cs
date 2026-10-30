using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Serilog;

using WebWayCMS.Data.Services;
using WebWayCMS.Mcp;

namespace WebWayCMS.Startup;

/// <summary>
/// Creates the <c>Mcp:SeedTokens</c> personal access tokens, so a development host (the TestHost, the
/// Playwright suite, curl checks) has a known MCP token. Development only, unless
/// <c>Mcp:AllowSeedTokensOutsideDevelopment</c> opts a throwaway Production-mode stack in: a seeded
/// plaintext lives in configuration, which is fine for a localhost throwaway and never for a real deployment.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class McpTokenSeeder
{
    internal static WebApplication EnsureMcpSeedTokens(this WebApplication app, bool throwOnError = false)
    {
        var logger = Log.ForContext(typeof(McpTokenSeeder));
        var options = app.Services.GetRequiredService<IOptions<McpOptions>>().Value;
        if (!options.Enabled || options.SeedTokens.Count == 0)
            return app;

        if (!app.Environment.IsDevelopment())
        {
            if (!options.AllowSeedTokensOutsideDevelopment)
            {
                logger.Warning("Ignoring {Count} Mcp:SeedTokens entries: seed tokens apply only in the Development environment.",
                    options.SeedTokens.Count);
                return app;
            }

            logger.Warning("Applying {Count} Mcp:SeedTokens entries in the {Environment} environment because Mcp:AllowSeedTokensOutsideDevelopment is set. Never set it on a real deployment.",
                options.SeedTokens.Count, app.Environment.EnvironmentName);
        }

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        try
        {
            var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
            var tokens = services.GetRequiredService<IApiTokenService>();

            foreach (var seed in options.SeedTokens)
            {
                var user = userManager.FindByEmailAsync(seed.UserEmail).GetAwaiter().GetResult();
                if (user == null)
                {
                    logger.Warning("MCP seed token {Name} skipped: no user with email {Email}.", seed.Name, seed.UserEmail);
                    continue;
                }

                var (success, error) = tokens.UpsertSeedTokenAsync(user.Id, seed.Name, seed.Token).GetAwaiter().GetResult();
                if (success)
                    logger.Information("MCP seed token {Name} is live for {Email}.", seed.Name, seed.UserEmail);
                else
                    logger.Warning("MCP seed token {Name} for {Email} not created: {Error}", seed.Name, seed.UserEmail, error);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error occurred seeding MCP tokens.");
            if (throwOnError)
                throw;
        }

        return app;
    }
}
