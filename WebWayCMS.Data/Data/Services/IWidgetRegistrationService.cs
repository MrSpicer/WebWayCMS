using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

public interface IWidgetRegistrationService
{
    Task<List<WidgetRegistrationDTO>> GetActiveAsync(CancellationToken ct = default);
    Task<WidgetRegistrationDTO?> GetByComponentNameAsync(string componentName, CancellationToken ct = default);
    Task<Dictionary<string, List<WidgetRegistrationDTO>>> GetActiveByCategoryAsync(CancellationToken ct = default);
}
