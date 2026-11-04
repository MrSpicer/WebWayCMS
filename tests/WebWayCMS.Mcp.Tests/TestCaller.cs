using System.Security.Claims;

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
