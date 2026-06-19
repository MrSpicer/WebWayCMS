namespace WebWayCMS.Controllers.Admin.Handlers;

public class AdminHandlerRegistry : IAdminHandlerRegistry
{
    private readonly Dictionary<string, IAdminCrudHandler> _handlers;

    public AdminHandlerRegistry(IEnumerable<IAdminCrudHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.ContentType, StringComparer.OrdinalIgnoreCase);
    }

    public IAdminCrudHandler? GetHandler(string contentType)
        => _handlers.GetValueOrDefault(contentType);
}
