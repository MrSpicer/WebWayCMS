namespace WebWayCMS.Controllers.Admin.Handlers;

public record AdminSaveResult(bool Success, string? ErrorMessage = null, string? ErrorField = null);