namespace WebWayCMS;

/// <summary>
/// Marks which CMS surface the host booted in: admin (<c>AddWebWayCmsAdmin</c>, which seeds roles and
/// maps the admin pipeline) or rendering-only (<c>AddWebWayCmsRendering</c>, neither). The draft preview
/// (<c>/_preview</c>) is admin-mode only — a rendering-only host never seeds the Admin/Editor roles and
/// has no admin pipeline, so serving drafts there would render a mixed draft/published page with no
/// authorized surface around it.
/// </summary>
public sealed class CmsDeploymentOptions
{
    /// <summary>True when the host registered the admin surface (<c>AddWebWayCmsAdmin</c>).</summary>
    public bool IsAdminMode { get; init; }
}
