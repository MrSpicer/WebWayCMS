using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

public interface IContentService<T> where T : class, IContent
{
    Task<List<T>> GetAllAsync(CancellationToken ct = default);
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<T?> GetByMasterIdAsync(Guid masterId, CancellationToken ct = default);
    Task<List<T>> GetAllVersionsAsync(Guid masterId, CancellationToken ct = default);
    Task<T> CreateAsync(T entity, CancellationToken ct = default);
    Task<bool> UpdateAsync(T entity, CancellationToken ct = default);
    Task<bool> UpsertAsync(T entity, CancellationToken ct = default);
    Task<T?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, bool softDelete = false, bool deleteHistory = false, CancellationToken ct = default);
    Task<List<T>> GetChildrenAsync(Guid parentMasterId, CancellationToken ct = default);
    Task<List<T>> GetRootsAsync(CancellationToken ct = default);
}
