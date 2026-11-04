namespace WebWayCMS.Data.Services;

/// <summary>
/// The metadata a historical zone item version can be saved back with today (the List→Single
/// grandfathering rule lives with the widget registry, outside the Data tier).
/// </summary>
public delegate string? RestorableMetadata(string componentName, string? historicalJson, string? currentJson);
