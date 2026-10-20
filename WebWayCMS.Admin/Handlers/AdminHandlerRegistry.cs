namespace WebWayCMS.Controllers.Admin.Handlers;

public class AdminHandlerRegistry : IAdminHandlerRegistry
{
    private readonly Dictionary<string, IAdminCrudHandler> _handlers;
    private readonly IReadOnlyCollection<IAdminCrudHandler> _all;

    public AdminHandlerRegistry(IEnumerable<IAdminCrudHandler> handlers)
    {
        _all = handlers.ToList();
        _handlers = _all.ToDictionary(h => h.ContentType, StringComparer.OrdinalIgnoreCase);
    }

    public IAdminCrudHandler? GetHandler(string contentType)
        => _handlers.GetValueOrDefault(contentType);

    public IReadOnlyCollection<IAdminCrudHandler> All => _all;
}
