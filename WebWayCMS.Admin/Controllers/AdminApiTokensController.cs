using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Data.Services;

namespace WebWayCMS.Controllers.Admin;

/// <summary>
/// Every user's MCP personal access tokens, for an Admin to audit and revoke. Users issue and revoke
/// their own on Identity → Manage → API tokens.
/// </summary>
[Authorize(Roles = "Admin")]
[Route("wadmin/apitokens")]
public class AdminApiTokensController : Controller
{
    private const string ViewPath = "~/Views/AdminApiTokens/Index.cshtml";

    private readonly IApiTokenService _tokens;

    public AdminApiTokensController(IApiTokenService tokens)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        return View(ViewPath, await _tokens.ListAllAsync(ct));
    }

    [HttpPost("{id:guid}/revoke")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        var actingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var (success, error) = await _tokens.RevokeAsync(id, actingUserId, actingIsAdmin: true, ct);
        if (success) TempData["Success"] = "The token was revoked.";
        else TempData["Error"] = error;
        return RedirectToAction(nameof(Index));
    }
}
