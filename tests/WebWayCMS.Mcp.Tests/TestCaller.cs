using System.Security.Claims;

using NSubstitute;

using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Mcp.Tests;

/// <summary>An <see cref="IMcpCaller"/> with a fixed principal holding the given roles.</summary>
internal sealed class TestCaller : IMcpCaller
{
    public static readonly TestCaller Admin = new("Admin");

    public static readonly TestCaller Editor = new("Editor");

    public static readonly TestCaller Anonymous = new(user: null);

    public TestCaller(params string[] roles)
        : this(new ClaimsPrincipal(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "Test")))
    {
    }

    private TestCaller(ClaimsPrincipal? user) => User = user;

    public ClaimsPrincipal? User { get; }
}

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
