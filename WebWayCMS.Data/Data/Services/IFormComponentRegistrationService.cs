using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

public interface IFormComponentRegistrationService
{
    Task<List<FormComponentRegistrationDTO>> GetActiveAsync(CancellationToken ct = default);
    Task<FormComponentRegistrationDTO?> GetByComponentNameAsync(string componentName, CancellationToken ct = default);
    Task<Dictionary<string, List<FormComponentRegistrationDTO>>> GetActiveByCategoryAsync(CancellationToken ct = default);
}
