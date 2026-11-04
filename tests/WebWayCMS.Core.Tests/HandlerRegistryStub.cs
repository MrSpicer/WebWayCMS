using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Core.Tests;

/// <summary>An in-memory <see cref="IAdminHandlerRegistry"/> (the real one lives in the Admin project).</summary>
internal sealed class HandlerRegistryStub(IEnumerable<IAdminCrudHandler> handlers) : IAdminHandlerRegistry
{
    private readonly List<IAdminCrudHandler> _handlers = handlers.ToList();

    public void Add(IAdminCrudHandler handler) => _handlers.Add(handler);

    public IAdminCrudHandler? GetHandler(string contentType)
        => _handlers.FirstOrDefault(h => string.Equals(h.ContentType, contentType, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyCollection<IAdminCrudHandler> All => _handlers;
}
