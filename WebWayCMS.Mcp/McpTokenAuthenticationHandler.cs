using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using WebWayCMS.Data.Services;

namespace WebWayCMS.Mcp;

/// <summary>
/// Authenticates an MCP request from a personal access token (<c>Authorization: Bearer wwc_…</c>) and
/// builds the token owner's principal, roles included, from the database on every request — so a role
/// change, a lockout or a revoke takes effect on the next call. Used only by the <c>/mcp</c> endpoint's
/// policy, never as a default scheme, so a browser cookie can't authenticate MCP and a token can't
/// authenticate the admin UI.
/// </summary>
/// <remarks>
/// Responses follow the MCP authorization spec's resource-server shape (the server verifies tokens it
/// did not issue through any OAuth flow): 401 with <c>WWW-Authenticate: Bearer</c> and the protected
/// resource metadata URL (<c>error="invalid_token"</c> when a token was presented), 403 with
/// <c>error="insufficient_scope"</c>, and 429 for a failed token once its client has too many failed attempts. The token
/// logic lives in the unit-tested <see cref="IApiTokenService"/>; this is wiring, validated by running.
/// </remarks>
[ExcludeFromCodeCoverage]
internal sealed class McpTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "McpToken";

    /// <summary>Claim carrying the id of the token that authenticated the request (for logs).</summary>
    public const string TokenIdClaimType = "wwc:token_id";

    /// <summary>RFC 9728 protected resource metadata path, served by the SDK's MCP authentication handler.</summary>
    public const string ResourceMetadataPath = "/.well-known/oauth-protected-resource";

    private const string ThrottledItemKey = "WebWayCMS:McpAuthThrottled";
    private const string BearerPrefix = "Bearer ";

    private readonly IApiTokenService _tokens;
    private readonly UserManager<IdentityUser> _users;
    private readonly IUserClaimsPrincipalFactory<IdentityUser> _principals;
    private readonly McpAuthFailureThrottle _throttle;

    public McpTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiTokenService tokens,
        UserManager<IdentityUser> users,
        IUserClaimsPrincipalFactory<IdentityUser> principals,
        McpAuthFailureThrottle throttle)
        : base(options, logger, encoder)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _principals = principals ?? throw new ArgumentNullException(nameof(principals));
        _throttle = throttle ?? throw new ArgumentNullException(nameof(throttle));
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        // The throttle is consulted only after a token failed: a blocked address (often a shared NAT or
        // proxy egress) still lets its valid tokens through, and only the bad ones get 429.
        var client = Context.Connection.RemoteIpAddress?.ToString();
        var token = await _tokens.ValidateAsync(header[BearerPrefix.Length..].Trim(), Context.RequestAborted);
        if (token == null)
        {
            if (_throttle.IsBlocked(client))
            {
                Context.Items[ThrottledItemKey] = true;
                return AuthenticateResult.Fail("Too many failed MCP authentication attempts.");
            }

            _throttle.RecordFailure(client);
            Serilog.Log.ForContext<McpTokenAuthenticationHandler>()
                .Warning("Rejected an MCP request with an unknown, revoked or expired token from {Client}", client);
            return AuthenticateResult.Fail("The access token is invalid, revoked or expired.");
        }

        var user = await _users.FindByIdAsync(token.UserId);
        if (user == null || await _users.IsLockedOutAsync(user))
            return AuthenticateResult.Fail("The token's user is missing or locked out.");

        var principal = await _principals.CreateAsync(user);
        var identity = new ClaimsIdentity(
            principal.Claims.Append(new Claim(TokenIdClaimType, token.Id.ToString())),
            SchemeName,
            ClaimTypes.Name,
            ClaimTypes.Role);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Items.ContainsKey(ThrottledItemKey))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            Response.Headers.RetryAfter = ((int)McpAuthFailureThrottle.Window.TotalSeconds).ToString();
            return;
        }

        // RFC 6750 §3.1: no error code when the request carried no credentials.
        var result = await HandleAuthenticateOnceSafeAsync();
        var error = result.Failure != null ? "invalid_token" : null;
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = BearerChallenge(error);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.Headers.WWWAuthenticate = BearerChallenge("insufficient_scope");
        return Task.CompletedTask;
    }

    private string BearerChallenge(string? error)
    {
        var metadata = $"{Request.Scheme}://{Request.Host}{Request.PathBase}{ResourceMetadataPath}";
        return error == null
            ? $"Bearer resource_metadata=\"{metadata}\""
            : $"Bearer error=\"{error}\", resource_metadata=\"{metadata}\"";
    }
}
