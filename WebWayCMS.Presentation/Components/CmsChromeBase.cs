using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Components;

namespace WebWayCMS.Presentation.Components;

/// <summary>
/// Optional base class for a host-provided public-site chrome component (see
/// <see cref="WebWayCMS.Attributes.CmsChromeAttribute"/>). Declares the <see cref="ChildContent"/>
/// contract the CMS document shell (<c>CmsLayout</c>) supplies the page body through; a host can
/// <c>@inherits CmsChromeBase</c> and render its header / navigation / footer around
/// <c>@ChildContent</c>. Logicless contract -&gt; excluded from coverage.
/// </summary>
[ExcludeFromCodeCoverage]
public abstract class CmsChromeBase : ComponentBase
{
    /// <summary>The public page body the chrome wraps. Supplied by the CMS document shell.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
