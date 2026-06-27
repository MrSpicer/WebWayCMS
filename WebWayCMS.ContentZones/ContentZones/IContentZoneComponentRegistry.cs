namespace WebWayCMS.ContentZones;

/// <summary>
/// Provides access to registered content zone components.
/// Implementations scan assemblies for ViewComponents decorated with
/// <see cref="Attributes.ContentZoneComponentAttribute"/> and build metadata.
/// </summary>
public interface IContentZoneComponentRegistry
{
    /// <summary>
    /// Gets all registered content zone components.
    /// </summary>
    /// <returns>A read-only list of component metadata.</returns>
    IReadOnlyList<ContentZoneComponentInfo> GetAllComponents();

    /// <summary>
    /// Gets a component by its name.
    /// </summary>
    /// <param name="componentName">The component name (without "ViewComponent" suffix).</param>
    /// <returns>The component info, or null if not found.</returns>
    ContentZoneComponentInfo? GetByName(string componentName);

    /// <summary>
    /// Gets all distinct categories of registered components.
    /// </summary>
    /// <returns>A list of category names, sorted alphabetically.</returns>
    IReadOnlyList<string> GetCategories();

    /// <summary>
    /// Gets all components in a specific category.
    /// </summary>
    /// <param name="category">The category name.</param>
    /// <returns>Components in the specified category, sorted by order then name.</returns>
    IReadOnlyList<ContentZoneComponentInfo> GetByCategory(string category);

    /// <summary>
    /// Gets components grouped by category.
    /// </summary>
    /// <returns>A dictionary mapping category names to their components.</returns>
    IReadOnlyDictionary<string, IReadOnlyList<ContentZoneComponentInfo>> GetComponentsByCategory();

    /// <summary>
    /// Creates a default configuration object for a component.
    /// </summary>
    /// <param name="componentName">The component name.</param>
    /// <returns>A new instance of the configuration type with default values, or null if no configuration type.</returns>
    object? CreateDefaultConfiguration(string componentName);

    /// <summary>
    /// Validates a configuration object against the component's property rules.
    /// </summary>
    /// <param name="componentName">The component name.</param>
    /// <param name="configuration">The configuration object or JSON string.</param>
    /// <returns>A list of validation errors, empty if valid.</returns>
    IReadOnlyList<string> ValidateConfiguration(string componentName, object configuration);
}