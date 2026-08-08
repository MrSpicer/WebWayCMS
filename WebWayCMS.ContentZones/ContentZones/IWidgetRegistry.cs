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
    void Invalidate();
}
