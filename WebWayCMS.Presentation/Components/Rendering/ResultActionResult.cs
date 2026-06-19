using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace WebWayCMS.Presentation.Rendering;

/// <summary>
/// Adapts a minimal-API <see cref="IResult"/> (such as a RazorComponentResult) so it can be
/// returned from an MVC controller action typed as <see cref="IActionResult"/>. This is the
/// bridge that lets the existing MVC page controllers render Blazor components.
/// </summary>
public sealed class ResultActionResult : IActionResult
{
    private readonly IResult _result;

    public ResultActionResult(IResult result)
        => _result = result ?? throw new ArgumentNullException(nameof(result));

    /// <summary>The wrapped result (exposed for testing/inspection).</summary>
    public IResult Result => _result;

    public Task ExecuteResultAsync(ActionContext context) => _result.ExecuteAsync(context.HttpContext);
}
