using WebWayCMS.Attributes;

namespace WebWayCMS.ContentZones;

public interface IWidgetRegistry
{
    IReadOnlyList<WidgetRegistrationInfo> GetAllComponents();
    WidgetRegistrationInfo? GetByName(string componentName);
    IReadOnlyList<string> GetCategories();
    IReadOnlyList<WidgetRegistrationInfo> GetByCategory(string category);
    IReadOnlyDictionary<string, IReadOnlyList<WidgetRegistrationInfo>> GetComponentsByCategory();
    object? CreateDefaultConfiguration(string componentName);
    IReadOnlyList<string> ValidateConfiguration(string componentName, object configuration);
    // Validates only the fields that apply to displayMode (FormProperty.DisplayModes); null checks every field.
    IReadOnlyList<string> ValidateConfiguration(string componentName, object configuration, WidgetDisplayMode? displayMode);
    void Invalidate();
}
