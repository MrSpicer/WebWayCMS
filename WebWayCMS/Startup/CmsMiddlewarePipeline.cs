using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Serilog;
using Serilog.Extensions.Hosting;

using WebWayCMS.Mcp;
using WebWayCMS.Routing;

namespace WebWayCMS.Startup;

internal static class CmsMiddlewarePipeline
{
    internal static WebApplication ConfigureRenderingPipeline(this WebApplication app)
    {
        ConfigureSharedMiddleware(app);
        app.MapCmsEndpoints();
        return app;
    }

    internal static WebApplication ConfigureAdminPipeline(this WebApplication app)
    {
        ConfigureSharedMiddleware(app);
        app.MapWebWayCmsMcp();
        app.MapCmsEndpoints();
        return app;
    }

    private static void ConfigureSharedMiddleware(WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseHsts();
        app.UseHttpsRedirection();

        // After the HTTPS redirect (so 307s aren't logged as full requests) and early enough to time the
        // whole pipeline. Serilog's request-logging middleware resolves DiagnosticContext from DI, which
        // only UseCmsSerilog()/AddSerilog registers — a host on the default MS logging providers gets no
        // summary line rather than a startup crash.
        if (app.Services.GetService<DiagnosticContext>() is not null)
            app.UseSerilogRequestLogging();

        var cspOptions = app.Services.GetRequiredService<IOptions<CspOptions>>().Value;
        var cspHeaderName = CspPolicyBuilder.HeaderName(cspOptions);
        var cspHeaderValue = CspPolicyBuilder.Build(cspOptions);

        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
            if (cspHeaderValue.Length > 0)
                context.Response.Headers[cspHeaderName] = cspHeaderValue;
            await next();
        });

        app.UseStaticFiles();

        // Authentication runs BEFORE UseRouting so that HttpContext.User is populated when the
        // DynamicRouteValueTransformer (CMSRouteTransformer) runs. The admin preview-aware read
        // context resolves drafts based on the authenticated user's role + preview cookie, and the
        // transformer calls into it during endpoint matching — which happens inside UseRouting.
        app.UseAuthentication();

        app.UseRouting();

        app.UseRateLimiter();

        app.UseAuthorization();
    }

    private static void MapCmsEndpoints(this WebApplication app)
    {
        app.MapRazorPages();

        app.MapControllers();

        app.MapDynamicControllerRoute<CMSRouteTransformer>("{**slug}");

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");
    }
}
