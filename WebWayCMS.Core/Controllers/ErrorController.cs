using System.Diagnostics;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Models;

namespace WebWayCMS.Controllers;

public class ErrorController : Controller
{
    private readonly Serilog.ILogger _logger = Serilog.Log.ForContext<ErrorController>();

    [Route("/Error")]
    public IActionResult Index()
    {
        // For exceptions handled by UseExceptionHandler
        var exFeature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        if (exFeature?.Error != null)
        {
            _logger.Error(exFeature.Error, "Unhandled exception occurred on path {Path}", exFeature.Path);
        }

        var model = new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier };
        // Use conventional view name so Razor finds /Views/Shared/Error.cshtml
        return View("Error", model);
    }

    [Route("Error/{statusCode}")]
    public IActionResult StatusCodeHandler(int statusCode)
    {
        // Log status codes like 404
        _logger.Warning("HTTP status code {StatusCode} returned for request {Path}", statusCode, HttpContext.Request.Path);
        var model = new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier };
        return View("Error", model);
    }
}