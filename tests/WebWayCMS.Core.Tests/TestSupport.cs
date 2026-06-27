using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Data;
using WebWayCMS.Mapping;

namespace WebWayCMS.Core.Tests;

internal static class TestSupport
{
    /// <summary>Builds a real IMapper from the production MappingProfile (covers the mapping code).</summary>
    public static IMapper CreateMapper() =>
        new MapperConfiguration(c => c.AddProfile(new MappingProfile())).CreateMapper();
}

/// <summary>
/// Builds the minimal MVC service graph and a configured ControllerContext so controller actions
/// (including TryUpdateModelAsync / ViewData) can run in isolation.
/// </summary>
internal sealed class MvcHarness
{
    public ServiceProvider Services { get; }

    public MvcHarness()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersWithViews();
        Services = services.BuildServiceProvider();
    }

    public DefaultHttpContext NewHttpContext(string[] roles, IQueryCollection? query = null)
    {
        var context = new DefaultHttpContext { RequestServices = Services };
        var identity = new ClaimsIdentity(
            roles.Select(r => new Claim(ClaimTypes.Role, r)),
            authenticationType: "Test");
        context.User = new ClaimsPrincipal(identity);
        if (query != null)
            context.Request.Query = query;
        return context;
    }

    public void Configure(ControllerBase controller, string[] roles, IQueryCollection? query = null)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = NewHttpContext(roles, query),
            RouteData = new RouteData(),
            ActionDescriptor = new ControllerActionDescriptor()
        };
    }
}