using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

internal static class TestSupport
{
    /// <summary>Builds a real IMapper from the production MappingProfile (covers the mapping code).</summary>
    public static IMapper CreateMapper() =>
        new MapperConfiguration(c => c.AddProfile(new MappingProfile())).CreateMapper();

    /// <summary>A relation binder that accepts every model (the binder has its own tests).</summary>
    public static IRelationBinder AcceptingBinder()
    {
        var binder = Substitute.For<IRelationBinder>();
        binder.ValidateAsync(Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((AdminSaveResult?)null);
        return binder;
    }

    /// <summary>A transaction that just runs its work (commit/rollback are the real one's, tested in Data).</summary>
    public static ICmsTransaction PassThroughTransaction()
    {
        var transaction = Substitute.For<ICmsTransaction>();
        transaction.RunAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(c => c.Arg<Func<CancellationToken, Task<bool>>>()(c.Arg<CancellationToken>()));
        return transaction;
    }

    /// <summary>
    /// A delete service that only runs the model's own delete (no relation descendants), so a model's
    /// <c>DeleteAsync</c> exercises its <c>DeleteSelfAsync</c>. The real cascade has its own tests.
    /// </summary>
    public static IContentDeleteService PassThroughDeletes() => new PassThroughDeleteService();

    private sealed class PassThroughDeleteService : IContentDeleteService
    {
        public async Task<(bool Success, string? ErrorMessage, int DeletedDescendants)> DeleteAsync(
            Guid nodeId, Func<Guid, CancellationToken, Task<bool>> deleteSelf, CancellationToken ct = default)
            => (await deleteSelf(nodeId, ct), null, 0);
    }
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
        context.Features.Set<IResponseCookiesFeature>(new ResponseCookiesFeature(context.Features));
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