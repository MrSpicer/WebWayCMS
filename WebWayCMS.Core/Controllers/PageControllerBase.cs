using WebWayCMS.Data.Models;
using Microsoft.AspNetCore.Mvc;

namespace WebWayCMS.Controllers;

/// <summary>
/// Base class for dynamic page controllers. Provides typed access to the page
/// data and configuration stored in HttpContext.Items by <see cref="Routing.PageRouteTransformer"/>.
/// </summary>
/// <typeparam name="TConfig">The configuration model type.</typeparam>
public abstract class PageControllerBase<TConfig> : Controller where TConfig : class, new()
{
    /// <summary>
    /// Gets the current page data stored by the route transformer.
    /// </summary>
    protected PageDTO? CurrentPage => HttpContext.Items["CMS:PageData"] as PageDTO;

    /// <summary>
    /// Gets the deserialized page configuration stored by the route transformer.
    /// </summary>
    protected TConfig PageConfig => HttpContext.Items["CMS:PageConfig"] as TConfig ?? new TConfig();

    /// <summary>
    /// The default action invoked by the dynamic route transformer.
    /// </summary>
    public abstract Task<IActionResult> Index();
}
