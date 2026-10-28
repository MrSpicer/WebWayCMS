namespace WebWayCMS.Data.Models;

/// <summary>How a route item's pattern is obtained.</summary>
public enum RouteMode
{
    /// <summary>The pattern is derived from the owner's slug and parent at publish time.</summary>
    Derived = 0,

    /// <summary>The route has a fixed pattern the user edits.</summary>
    Explicit = 1
}
