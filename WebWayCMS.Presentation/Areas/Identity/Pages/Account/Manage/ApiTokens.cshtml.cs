#nullable enable

using System.Text.Json;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;

namespace WebWayCMS.Presentation.Areas.Identity.Pages.Account.Manage
{
    /// <summary>
    /// Lets an Admin issue and revoke their own MCP personal access tokens. Admin-only because the MCP
    /// endpoint accepts only Admins; 404 on a rendering-only host, which has no token service (and no MCP).
    /// </summary>
    [Authorize(Roles = PreviewConstants.AdminRole)]
    public class ApiTokensModel : PageModel
    {
        /// <summary>The expiry choices offered on the create form, in days (0 = never).</summary>
        public static readonly int[] ExpiryDayOptions = [0, 30, 90, 365];

        private readonly UserManager<IdentityUser> _userManager;
        private readonly IConfiguration _configuration;

        public ApiTokensModel(UserManager<IdentityUser> userManager, IConfiguration configuration)
        {
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public IReadOnlyList<ApiTokenDTO> Tokens { get; private set; } = [];

        /// <summary>The plaintext of a token just created; shown once, never stored.</summary>
        public string? NewToken { get; private set; }

        /// <summary>The name of the token just created.</summary>
        public string? NewTokenName { get; private set; }

        /// <summary>A ready-to-paste <c>.mcp.json</c> for <see cref="NewToken"/>.</summary>
        public string? McpJsonSnippet { get; private set; }

        [BindProperty]
        public string? Name { get; set; }

        [BindProperty]
        public int ExpiresInDays { get; set; } = 90;

        [TempData]
        public string? StatusMessage { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            var tokens = TokenService;
            if (tokens == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            if (userId == null) return NotFound();

            Tokens = await tokens.ListForUserAsync(userId, ct);
            return Page();
        }

        public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
        {
            var tokens = TokenService;
            if (tokens == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            if (userId == null) return NotFound();

            // Anything off the list (a tampered or stale form) is refused, never read as "never expires".
            if (!ExpiryDayOptions.Contains(ExpiresInDays))
            {
                ErrorMessage = "Choose an expiry from the list.";
                return RedirectToPage();
            }

            DateTime? expires = ExpiresInDays > 0 ? DateTime.UtcNow.AddDays(ExpiresInDays) : null;

            var result = await tokens.CreateAsync(userId, Name ?? string.Empty, expires, ct);
            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage;
                return RedirectToPage();
            }

            // Rendered straight from this POST (no redirect), so the plaintext never goes through TempData;
            // the success message is the new-token box itself (a [TempData] StatusMessage set here would
            // never be read and would flash again on the next visit). no-store keeps the plaintext out of
            // the browser's cache and history.
            Response.Headers.CacheControl = "no-store";
            NewToken = result.PlainToken;
            NewTokenName = result.Token!.Name;
            McpJsonSnippet = BuildMcpJson(result.PlainToken!);
            Name = null;
            Tokens = await tokens.ListForUserAsync(userId, ct);
            return Page();
        }

        public async Task<IActionResult> OnPostRevokeAsync(Guid id, CancellationToken ct)
        {
            var tokens = TokenService;
            if (tokens == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            if (userId == null) return NotFound();

            // Own tokens only here; an Admin revokes other users' tokens at /wadmin/apitokens.
            var (success, error) = await tokens.RevokeAsync(id, userId, actingIsAdmin: false, ct);
            if (success) StatusMessage = "The token was revoked.";
            else ErrorMessage = error;
            return RedirectToPage();
        }

        /// <summary>Registered in admin mode only.</summary>
        private IApiTokenService? TokenService => HttpContext.RequestServices.GetService<IApiTokenService>();

        private string BuildMcpJson(string token)
        {
            var path = _configuration["Mcp:Path"];
            var url = $"{Request.Scheme}://{Request.Host}{Request.PathBase}{(string.IsNullOrEmpty(path) ? "/mcp" : path)}";
            var config = new Dictionary<string, object>
            {
                ["mcpServers"] = new Dictionary<string, object>
                {
                    ["webwaycms"] = new Dictionary<string, object>
                    {
                        ["type"] = "http",
                        ["url"] = url,
                        ["headers"] = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
                    },
                },
            };
            return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
