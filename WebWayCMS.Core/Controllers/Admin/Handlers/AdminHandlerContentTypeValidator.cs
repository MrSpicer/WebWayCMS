using WebWayCMS.Data.Services.ContentTypes;

namespace WebWayCMS.Controllers.Admin.Handlers;

/// <summary>
/// Boot check (admin mode): every handler that edits a content type (<see cref="IAdminCrudHandler.IsContentType"/>)
/// must name a key registered with a content store, so a misspelt handler key stops the app instead of writing
/// nodes no registration describes.
/// </summary>
public static class AdminHandlerContentTypeValidator
{
    public static void Validate(IAdminHandlerRegistry handlers, IContentTypeRegistry contentTypes)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(contentTypes);

        foreach (var handler in handlers.All)
        {
            if (handler.IsContentType && contentTypes.Get(handler.ContentType) == null)
                throw new InvalidOperationException(
                    $"Admin handler {handler.GetType().Name} edits content type '{handler.ContentType}', which is not registered with a content store (IWebWayCmsBuilder.AddContentType).");
        }
    }
}
