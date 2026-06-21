using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

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

        return ErrorContent();
    }

    [Route("Error/{statusCode}")]
    public IActionResult StatusCodeHandler(int statusCode)
    {
        // Log status codes like 404
        _logger.Warning("HTTP status code {StatusCode} returned for request {Path}", statusCode, HttpContext.Request.Path);
        return ErrorContent();
    }

    // Self-contained HTML error page. The CMS view layer is Blazor; this fallback intentionally has
    // no Razor view or layout dependency so it renders even when component rendering is what failed.
    private ContentResult ErrorContent()
    {
        var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var html = $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>Error</title>
            </head>
            <body>
                <main role="main">
                    <h1>Error.</h1>
                    <h2>An error occurred while processing your request.</h2>
                    <p>Request ID: <code>{{WebUtility.HtmlEncode(requestId)}}</code></p>
                </main>
            </body>
            </html>
            """;
        return Content(html, "text/html");
    }
}
