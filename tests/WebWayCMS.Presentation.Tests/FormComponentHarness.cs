using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using WebWayCMS.Attributes;
using WebWayCMS.Services;

namespace WebWayCMS.Presentation.Tests;

/// <summary>
/// Attaches a form component with a real <see cref="FormFieldViewResolver"/> (over a substitute
/// <see cref="IViewDiscoveryService"/>) registered in HttpContext.RequestServices, mirroring the
/// production DI wiring. The discovery reports the shipped views (<c>adminwrite.default</c> and
/// <c>adminread.default</c>) for every component, and the request path is an admin path unless
/// <c>path</c> says otherwise.
/// </summary>
internal static class FormComponentHarness
{
    public sealed class Context
    {
        public Context(IViewDiscoveryService discovery)
        {
            Discovery = discovery;
        }

        public IViewDiscoveryService Discovery { get; }

        /// <summary>Registers a (component, mode, variant) hit resolving to the full view file name.</summary>
        public void Hit(string component, FormFieldRenderMode mode, string? variant, string view)
            => Discovery.TryResolveView(component, mode, variant, out var viewName)
                .Returns(x => { x[3] = view; return true; });
    }

    public static Context Attach(ViewComponent component, string path = "/wadmin/test", Action<IServiceCollection>? configure = null)
    {
        var discovery = Substitute.For<IViewDiscoveryService>();
        discovery.TryResolveView(Arg.Any<string>(), FormFieldRenderMode.AdminWrite, "default", out Arg.Any<string?>())
            .Returns(x => { x[3] = "adminwrite.default"; return true; });
        discovery.TryResolveView(Arg.Any<string>(), FormFieldRenderMode.AdminRead, "default", out Arg.Any<string?>())
            .Returns(x => { x[3] = "adminread.default"; return true; });

        var httpContext = ViewComponentHarness.Attach(component, services =>
        {
            services.AddSingleton<IFormFieldViewResolver>(new FormFieldViewResolver(discovery));
            services.AddSingleton<ICompositeViewEngine>(Substitute.For<ICompositeViewEngine>());
            configure?.Invoke(services);
        });
        httpContext.Request.Path = path;

        return new Context(discovery);
    }
}
