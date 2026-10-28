using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace WebWayCMS.Controllers.Admin;

/// <summary>
/// Marks a create/edit request as one rendered inside the reference picker's modal: the layout drops
/// the admin chrome, and a successful POST answers <c>{ id, title }</c> JSON instead of redirecting.
/// </summary>
public static class AdminModal
{
    /// <summary><see cref="ViewDataDictionary"/> key set when the request is a modal request.</summary>
    public const string ViewDataKey = "AdminModal";

    /// <summary>The query-string flag that opens a modal form (<c>?modal=1</c>).</summary>
    public const string QueryKey = "modal";

    /// <summary>True when <paramref name="request"/> asked for the modal form.</summary>
    public static bool IsModalRequest(HttpRequest request)
        => request.Query[QueryKey] == "1";

    /// <summary>True when the view is being rendered as a modal.</summary>
    public static bool IsModal(ViewDataDictionary viewData)
        => viewData[ViewDataKey] as bool? == true;
}
