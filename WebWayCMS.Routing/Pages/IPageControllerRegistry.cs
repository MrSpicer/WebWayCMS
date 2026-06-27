namespace WebWayCMS.Pages;

/// <summary>
/// Provides access to registered page controllers.
/// Implementations scan assemblies for Controllers decorated with
/// <see cref="Attributes.PageControllerAttribute"/> and build metadata.
/// </summary>
public interface IPageControllerRegistry
{
    IReadOnlyList<PageControllerInfo> GetAllControllers();
    PageControllerInfo? GetByName(string controllerName);
    IReadOnlyList<string> GetCategories();
    IReadOnlyList<PageControllerInfo> GetByCategory(string category);
    object? CreateDefaultConfiguration(string controllerName);
    IReadOnlyList<string> ValidateConfiguration(string controllerName, object configuration);
}