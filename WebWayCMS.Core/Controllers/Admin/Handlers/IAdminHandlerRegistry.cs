namespace WebWayCMS.Controllers.Admin.Handlers;

public interface IAdminHandlerRegistry
{
    IAdminCrudHandler? GetHandler(string contentType);

    /// <summary>Every registered handler, in registration order.</summary>
    IReadOnlyCollection<IAdminCrudHandler> All { get; }
}
