namespace WebWayCMS.Presentation.Components.Admin;

/// <summary>The outcome of the add/edit widget form: the chosen component and its serialized configuration.</summary>
public sealed record ContentZoneItemFormResult(string ComponentName, string Json);
