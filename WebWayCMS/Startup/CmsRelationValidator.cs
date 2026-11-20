using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Services.ContentTypes;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsRelationValidator
{
    /// <summary>
    /// Admin-mode boot checks of the content type keys code declares: every content-type admin handler names a
    /// key registered with a content store (<see cref="AdminHandlerContentTypeValidator"/>), and the
    /// <see cref="ContentRelationRegistry"/> builds once, so an invalid <c>[ContentRelation]</c> declaration (a
    /// many containment, a target with no handler or no content store, …) stops the app instead of surfacing on
    /// the first delete.
    /// </summary>
    internal static WebApplication EnsureContentRelationsValid(this WebApplication app, bool throwOnError = false)
    {
        using var scope = app.Services.CreateScope();
        try
        {
            AdminHandlerContentTypeValidator.Validate(
                scope.ServiceProvider.GetRequiredService<IAdminHandlerRegistry>(),
                scope.ServiceProvider.GetRequiredService<IContentTypeRegistry>());
            scope.ServiceProvider.GetRequiredService<ContentRelationRegistry>().Validate();
        }
        catch (Exception ex)
        {
            Log.ForContext(typeof(CmsRelationValidator)).Error(ex, "Invalid content type key or content relation declaration.");
            if (throwOnError)
            {
                throw;
            }
        }

        return app;
    }
}
