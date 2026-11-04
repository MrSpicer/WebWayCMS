namespace WebWayCMS.Models.PageHistory;

/// <summary>How a zone item differs between two states of a page set.</summary>
public enum PageSetChange
{
    Unchanged = 0,
    Added = 1,
    Removed = 2,

    /// <summary>The widget, its configuration or its metadata changed.</summary>
    Changed = 3,

    /// <summary>Only its zone or position changed.</summary>
    Moved = 4
}
