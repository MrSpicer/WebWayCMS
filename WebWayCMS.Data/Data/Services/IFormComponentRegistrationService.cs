using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

public interface IFormComponentRegistrationService
{
    Task<List<FormComponentRegistrationDTO>> GetActiveAsync(CancellationToken ct = default);

    Task<List<FormComponentRegistrationDTO>> GetAllAsync(CancellationToken ct = default);

    Task<FormComponentRegistrationDTO?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<FormComponentRegistrationDTO?> GetByComponentNameAsync(string componentName, CancellationToken ct = default);

    Task<Dictionary<string, List<FormComponentRegistrationDTO>>> GetActiveByCategoryAsync(CancellationToken ct = default);

    Task<(bool Success, string? ErrorMessage, FormComponentRegistrationDTO? Registration)> UpsertAsync(
        FormComponentRegistrationDTO registration, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
