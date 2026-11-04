using NSubstitute;

using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Mcp.Tests;

internal static class HandlerRoleExtensions
{
    /// <summary>
    /// Gives a substitute handler the production default (null role lists: Admin only). NSubstitute
    /// would otherwise return empty arrays, which grant no one.
    /// </summary>
    public static void AdminOnly(this IAdminCrudHandler handler)
    {
        handler.WriteRoles.Returns((string[]?)null);
        handler.PublishRoles.Returns((string[]?)null);
    }
}
