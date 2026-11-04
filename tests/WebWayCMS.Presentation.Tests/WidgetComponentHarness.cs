using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Services;

namespace WebWayCMS.Presentation.Tests;

/// <summary>
/// Attaches a widget component with an <see cref="IWidgetViewResolver"/> (over a substitute
/// <see cref="IViewDiscoveryService"/>) and a substitute <see cref="IWidgetRegistry"/> registered
/// in HttpContext.RequestServices, mirroring the production DI wiring.
/// </summary>
internal static class WidgetComponentHarness
{
    public sealed class Context
    {
        public Context(IViewDiscoveryService discovery, IWidgetRegistry registry)
        {
            Discovery = discovery;
            Registry = registry;
        }

        public IViewDiscoveryService Discovery { get; }
        public IWidgetRegistry Registry { get; }

        /// <summary>Registers a (component, mode, variant) hit resolving to the full view file name.</summary>
        public void Hit(string component, WidgetRenderMode mode, string? variant, string view)
            => Discovery.TryResolveView(component, mode, variant, out var viewName)
                .Returns(x => { x[3] = view; return true; });
    }

    public static Context Attach(ViewComponent component)
    {
        var discovery = Substitute.For<IViewDiscoveryService>();
        var registry = Substitute.For<IWidgetRegistry>();
        var resolver = new WidgetViewResolver(discovery);

        // Once HttpContext.RequestServices is non-null, ViewComponent.View() resolves the view
        // engine from it eagerly, so a substitute must be registered alongside the widget services.
        ViewComponentHarness.Attach(component, services =>
        {
            services.AddSingleton<IWidgetViewResolver>(resolver);
            services.AddSingleton<IWidgetRegistry>(registry);
            services.AddSingleton<ICompositeViewEngine>(Substitute.For<ICompositeViewEngine>());
        });

        return new Context(discovery, registry);
    }
}
