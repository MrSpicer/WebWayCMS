namespace WebWayCMS.Data.Models;

/// <summary>
/// The catalog fields every component registration shares (widgets, page types, form components): how the
/// admin lists, groups and orders it.
/// </summary>
public interface IRegistrationDescriptor
{
    string DisplayName { get; }
    string Category { get; }
    string IconClass { get; }
    int Order { get; }
    bool IsActive { get; }
}
