using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace WebWayCMS.Controllers.Admin;

/// <summary>
/// The <c>returnUrl</c> an admin edit screen carries: from the query string (the link that opened it) or, when a
/// failed save re-renders the form, from the posted form. Only a local URL is returned.
/// </summary>
public static class AdminReturnUrl
{
    public const string FieldName = "returnUrl";

    public static string? Resolve(HttpRequest request, IUrlHelper url)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(url);

        string? candidate = request.Query[FieldName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(candidate) && request.HasFormContentType)
            candidate = request.Form[FieldName].FirstOrDefault();

        return url.LocalOrDefault(candidate);
    }
}
