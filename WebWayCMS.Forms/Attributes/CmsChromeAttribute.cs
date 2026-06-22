namespace WebWayCMS.Attributes;

/// <summary>
/// Marks a Blazor component as the public-site "chrome" (header / navigation / footer) that wraps all
/// public page content. A host project consuming the CMS drops a single component decorated with this
/// attribute into its entry assembly to brand the public site; the CMS document shell
/// (<c>CmsLayout</c>) owns the surrounding HTML document and framework script, and renders the chrome
/// around the page body.
/// </summary>
/// <remarks>
/// The chrome component must expose a <c>[Parameter] RenderFragment? ChildContent</c> property; the
/// page body is supplied through it. When more than one chrome component is present, the one with the
/// lowest <see cref="Order"/> wins (ties broken by full type name); when none is present, no chrome is
/// applied and the page body renders directly inside the document shell.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CmsChromeAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the selection order when multiple chrome components are present.
    /// Lower values win. Default is 0.
    /// </summary>
    public int Order { get; set; } = 0;
}
