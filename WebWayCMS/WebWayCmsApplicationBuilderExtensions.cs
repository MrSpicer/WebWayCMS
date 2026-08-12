using Microsoft.AspNetCore.Builder;

using WebWayCMS.Startup;

namespace WebWayCMS;

public static class WebWayCmsApplicationBuilderExtensions
{
    /// <summary>
    /// Applies pending migrations, seeds the default home page, and configures the rendering
    /// middleware pipeline. Does NOT seed admin roles/user, the admin page, or MCP.
    /// </summary>
    public static WebApplication UseWebWayCmsRendering(this WebApplication app, bool throwOnError = true)
    {
        app.ApplyCmsPendingMigrations(throwOnError);
        app.EnsureDefaultHomePage(false, throwOnError);
        app.EnsureWidgetRegistrationsSeeded(throwOnError);
        app.EnsureFormComponentRegistrationsSeeded(throwOnError);
        app.EnsurePageControllerRegistrationsSeeded(throwOnError);
        app.EnsureCodeBasedRoutesSeeded(throwOnError);
        app.ConfigureRenderingPipeline();
        return app;
    }

    /// <summary>
    /// Applies pending migrations, seeds roles/admin user and default pages (home + admin),
    /// and configures the full admin middleware pipeline including MCP.
    /// </summary>
    public static WebApplication UseWebWayCmsAdmin(this WebApplication app, bool throwOnError = true)
    {
        app.ApplyCmsPendingMigrations(throwOnError);
        app.EnsureCmsRolesAndAdminSeeded(throwOnError);
        app.EnsureDefaultHomePage(true, throwOnError);
        app.EnsureWidgetRegistrationsSeeded(throwOnError);
        app.EnsureFormComponentRegistrationsSeeded(throwOnError);
        app.EnsurePageControllerRegistrationsSeeded(throwOnError);
        app.EnsureCodeBasedRoutesSeeded(throwOnError);
        app.ConfigureAdminPipeline();
        return app;
    }

    /// <summary>
    /// Backwards-compatible entry point. Delegates to <see cref="UseWebWayCmsAdmin"/>.
    /// </summary>
    public static WebApplication UseWebWayCms(this WebApplication app, bool throwOnError = true)
    {
        return UseWebWayCmsAdmin(app, throwOnError);
    }
}
