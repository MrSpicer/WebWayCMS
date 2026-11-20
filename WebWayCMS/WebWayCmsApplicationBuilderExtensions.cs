using Microsoft.AspNetCore.Builder;

using WebWayCMS.Startup;

namespace WebWayCMS;

public static class WebWayCmsApplicationBuilderExtensions
{
    /// <summary>
    /// Applies pending migrations, seeds the code-based registrations (content types, widgets, form components, page types), and configures the rendering
    /// middleware pipeline. Seeds no content (the admin host sharing the database seeds the pages), and
    /// no admin roles/user or MCP.
    /// </summary>
    public static WebApplication UseWebWayCmsRendering(this WebApplication app, bool throwOnError = true)
    {
        app.ApplyCmsPendingMigrations(throwOnError);
        app.EnsureContentTypeRegistrationsSeeded(throwOnError);
        app.EnsureWidgetRegistrationsSeeded(throwOnError);
        app.EnsureFormComponentRegistrationsSeeded(throwOnError);
        app.EnsurePageControllerRegistrationsSeeded(throwOnError);
        app.EnsureCodeBasedRoutesSeeded(throwOnError);
        app.ConfigureRenderingPipeline();
        return app;
    }

    /// <summary>
    /// Applies pending migrations, seeds roles/admin user and the JSON content seed (which includes the
    /// CMS's own Home, Dashboard, Users and Roles pages), and configures the full admin middleware pipeline including MCP.
    /// </summary>
    public static WebApplication UseWebWayCmsAdmin(this WebApplication app, bool throwOnError = true)
    {
        app.ApplyCmsPendingMigrations(throwOnError);
        app.EnsureContentRelationsValid(throwOnError);
        app.EnsureCmsRolesAndAdminSeeded(throwOnError);
        app.EnsureMcpSeedTokens(throwOnError);
        app.EnsureContentTypeRegistrationsSeeded(throwOnError);
        app.EnsureWidgetRegistrationsSeeded(throwOnError);
        app.EnsureFormComponentRegistrationsSeeded(throwOnError);
        app.EnsurePageControllerRegistrationsSeeded(throwOnError);
        app.EnsureCodeBasedRoutesSeeded(throwOnError);
        app.EnsureJsonContentSeeded(throwOnError);
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
