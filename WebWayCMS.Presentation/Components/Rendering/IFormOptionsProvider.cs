using WebWayCMS.Forms;

namespace WebWayCMS.Presentation.Rendering;

/// <summary>
/// Supplies the selectable options for picker-style form fields - ViewPicker (available views) and
/// Guid entity pickers (ContentBlock/Article/ArticleList/ContentZone). Returns an empty list for
/// fields that are not picker-backed, in which case the form renders a plain input.
/// </summary>
public interface IFormOptionsProvider
{
    Task<IReadOnlyList<FormOption>> GetOptionsAsync(FormPropertyInfo prop, CancellationToken ct = default);
}
