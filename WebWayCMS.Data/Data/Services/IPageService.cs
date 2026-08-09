using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

public interface IPageService
{
    Task<List<PageDTO>> GetAllAsync(CancellationToken ct = default);
    Task<PageDTO?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<PageDTO>> GetAllVersionsAsync(Guid masterId, CancellationToken ct = default);
    Task<PageDTO> CreateAsync(PageDTO page, CancellationToken ct = default);
    Task<bool> UpdateAsync(PageDTO page, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default);
}
