namespace WebWayCMS.Attributes;

/// <summary>
/// The render context for a form field view. A form component's view files are named
/// <c>{mode}.{variant}.cshtml</c> (mode lowercase), and exactly one mode is active per render. The
/// mode is never chosen directly: read vs. write comes from the form's <c>FormFieldMode</c>, and
/// admin vs. public from the request path (under <c>/wadmin</c> is admin).
///
/// Modes are CMS-owned and fixed; hosts cannot add new ones.
/// </summary>
public enum FormFieldRenderMode
{
    /// <summary>
    /// Public editable field (the default).
    /// </summary>
    Write = 0,

    /// <summary>
    /// Public read-only display of a field's value.
    /// </summary>
    Read,

    /// <summary>
    /// Editable field on an admin page, styled for the admin rather than the site.
    /// </summary>
    AdminWrite,

    /// <summary>
    /// Read-only display of a field's value on an admin page.
    /// </summary>
    AdminRead
}
