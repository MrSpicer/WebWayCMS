namespace WebWayCMS.Services;

/// <summary>
/// The outcome of resolving a widget render to a view.
/// </summary>
public enum WidgetViewResolutionKind
{
    /// <summary>A concrete view file was resolved; render it (<see cref="WidgetViewResolution.ViewName"/>).</summary>
    View = 0,

    /// <summary>Edit mode with no edit-mode views at all; render the generic CMS card.</summary>
    EditCard,

    /// <summary>View mode with no view-mode views; render nothing.</summary>
    Empty
}
