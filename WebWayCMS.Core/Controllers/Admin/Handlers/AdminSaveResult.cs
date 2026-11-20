using WebWayCMS.Data.Services;

namespace WebWayCMS.Controllers.Admin.Handlers;

public record AdminSaveResult(bool Success, string? ErrorMessage = null, string? ErrorField = null, Guid? NodeId = null)
{
    /// <summary>A store write's outcome as an admin result: success, or failure with the store's message.</summary>
    public static AdminSaveResult FromWrite(ContentWriteResult result)
        => result.Success ? new AdminSaveResult(true) : new AdminSaveResult(false, result.ErrorMessage);
}
