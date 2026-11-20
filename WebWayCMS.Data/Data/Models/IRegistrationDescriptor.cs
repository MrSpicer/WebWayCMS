namespace WebWayCMS.Data.Models;

/// <summary>
/// A component registration (widgets, page types, form components): the catalog fields plus the switch that
/// takes the component out of the admin's choices.
/// </summary>
public interface IRegistrationDescriptor : IRegistrationCatalog
{
    bool IsActive { get; }
}
