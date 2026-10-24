using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Services;

namespace WebWayCMS.ViewComponents.SlotDiscovery;

/// <summary>
/// Discovers a container widget's slots by invoking its view component with a
/// <see cref="SlotDiscoveryRecorder"/> in ViewData and the output discarded: each top-level
/// <c>ContentZone</c> its view would show is recorded instead of rendered. Runs in the current request,
/// or — with none (the startup content seeder) — in a stand-in context over this scope's services.
/// </summary>
public sealed class ViewComponentSlotDiscovery : IWidgetSlotDiscovery
{
    private readonly Serilog.ILogger _logger = Serilog.Log.ForContext<ViewComponentSlotDiscovery>();

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IServiceProvider _services;

    public ViewComponentSlotDiscovery(IHttpContextAccessor httpContextAccessor, IServiceProvider services)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public async Task<IReadOnlyList<string>?> DiscoverSlotsAsync(Guid itemNodeId, string componentName, object componentProperties, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var httpContext = _httpContextAccessor.HttpContext ?? new DefaultHttpContext { RequestServices = _services };
            var recorder = new SlotDiscoveryRecorder(itemNodeId);

            var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            {
                [SlotDiscoveryRecorder.ViewDataKey] = recorder,
                ["ContentZone:ParentItemId"] = itemNodeId
            };
            var actionContext = new ActionContext(httpContext, httpContext.GetRouteData(), new ActionDescriptor());
            var viewContext = new ViewContext(
                actionContext,
                NullView.Instance,
                viewData,
                httpContext.RequestServices.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(httpContext),
                TextWriter.Null,
                new HtmlHelperOptions());

            var helper = httpContext.RequestServices.GetRequiredService<IViewComponentHelper>();
            ((IViewContextAware)helper).Contextualize(viewContext);

            // Rendering the result drives the view, and with it the ContentZone invocations that record.
            var content = await helper.InvokeAsync(componentName, componentProperties);
            content.WriteTo(TextWriter.Null, HtmlEncoder.Default);
            return recorder.Slots;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "Slot discovery failed for {Component} item {ItemNodeId}.", componentName, itemNodeId);
            return null;
        }
    }

    /// <summary>The view a discovery <see cref="ViewContext"/> belongs to; it is never rendered.</summary>
    private sealed class NullView : IView
    {
        public static readonly NullView Instance = new();

        public string Path => string.Empty;

        public Task RenderAsync(ViewContext context) => Task.CompletedTask;
    }
}
