using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Services;

namespace WebWayCMS.Presentation.Tests;

internal static class ViewComponentHarness
{
    /// <summary>
    /// Attaches a usable ViewComponentContext so HttpContext, ViewData, View() and Content() work.
    /// When <paramref name="configure"/> is supplied, a service provider is built from it and
    /// installed on HttpContext.RequestServices (widget components resolve their
    /// <c>IWidgetViewResolver</c> from there).
    /// </summary>
    public static DefaultHttpContext Attach(ViewComponent component, Action<IServiceCollection>? configure = null)
    {
        var httpContext = new DefaultHttpContext();
        if (configure != null)
        {
            var services = new ServiceCollection();
            configure(services);
            httpContext.RequestServices = services.BuildServiceProvider();
        }

        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary());
        var tempData = new TempDataDictionary(httpContext, Substitute.For<ITempDataProvider>());
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var viewContext = new ViewContext(
            actionContext,
            Substitute.For<IView>(),
            viewData,
            tempData,
            TextWriter.Null,
            new HtmlHelperOptions());

        component.ViewComponentContext = new ViewComponentContext(
            new ViewComponentDescriptor(),
            new Dictionary<string, object?>(),
            HtmlEncoder.Default,
            viewContext,
            TextWriter.Null);

        return httpContext;
    }

    public static string? ViewName(IViewComponentResult result) => ((ViewViewComponentResult)result).ViewName;

    public static object? Model(IViewComponentResult result) => ((ViewViewComponentResult)result).ViewData!.Model;
}

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
