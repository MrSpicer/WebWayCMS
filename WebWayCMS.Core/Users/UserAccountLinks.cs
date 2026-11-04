using Microsoft.AspNetCore.Http;

namespace WebWayCMS.Users;

/// <summary>
/// Builds the links from the current request's scheme, host and path base, the same origin the scaffolded
/// Register / ForgotPassword pages put in their emails. Every caller (an admin form post, an MCP call) is an
/// HTTP request.
/// </summary>
public sealed class UserAccountLinks : IUserAccountLinks
{
    internal const string AccountPath = "/Identity/Account/";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserAccountLinks(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public string ConfirmEmail(string userId, string code) =>
        Build("ConfirmEmail", ("userId", userId), ("code", code));

    public string ResetPassword(string code) =>
        Build("ResetPassword", ("code", code));

    public string AcceptInvitation(string userId, string code) =>
        Build("AcceptInvitation", ("userId", userId), ("code", code));

    private string Build(string page, params (string Name, string Value)[] query)
    {
        var request = _httpContextAccessor.HttpContext?.Request
            ?? throw new InvalidOperationException("Account links can only be built during an HTTP request.");
        var queryString = string.Join("&", query.Select(q => $"{q.Name}={Uri.EscapeDataString(q.Value)}"));
        return $"{request.Scheme}://{request.Host}{request.PathBase}{AccountPath}{page}?{queryString}";
    }
}
