using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Reads and writes the JSON content seed ledger, mapping a stable seed key to the node it produced.
/// </summary>
public interface IContentSeedRecordService
{
    Task<ContentSeedRecordDTO?> GetAsync(Guid seedId, CancellationToken ct = default);

    Task UpsertAsync(ContentSeedRecordDTO record, CancellationToken ct = default);
}
