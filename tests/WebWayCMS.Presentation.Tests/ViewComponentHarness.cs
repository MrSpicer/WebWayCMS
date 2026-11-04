using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

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
