using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

using WebWayCMS.Services.Relations;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsRelationValidator
{
    /// <summary>
    /// Builds the <see cref="ContentRelationRegistry"/> once at boot (admin mode), so an invalid
    /// <c>[ContentRelation]</c> declaration (a many containment, an unregistered target, …) stops the
    /// app instead of surfacing on the first delete.
    /// </summary>
    internal static WebApplication EnsureContentRelationsValid(this WebApplication app, bool throwOnError = false)
    {
        using var scope = app.Services.CreateScope();
        try
        {
            scope.ServiceProvider.GetRequiredService<ContentRelationRegistry>().Validate();
        }
        catch (Exception ex)
        {
            Log.ForContext(typeof(CmsRelationValidator)).Error(ex, "Invalid content relation declaration.");
            if (throwOnError)
            {
                throw;
            }
        }

        return app;
    }
}
