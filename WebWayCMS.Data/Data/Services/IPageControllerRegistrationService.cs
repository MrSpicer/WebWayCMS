using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

public interface IPageControllerRegistrationService
{
    Task<List<PageControllerRegistrationDTO>> GetActiveAsync(CancellationToken ct = default);
    Task<PageControllerRegistrationDTO?> GetByControllerNameAsync(string controllerName, CancellationToken ct = default);
    Task<Dictionary<string, List<PageControllerRegistrationDTO>>> GetActiveByCategoryAsync(CancellationToken ct = default);
}
