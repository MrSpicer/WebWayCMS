namespace WebWayCMS.Data.Models;

/// <summary>
/// The catalog fields every registration shares (widgets, page types, form components, content types): how the
/// admin lists, groups and orders it.
/// </summary>
public interface IRegistrationCatalog
{
    string DisplayName { get; }
    string Category { get; }
    string IconClass { get; }
    int Order { get; }
}
