using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Models.Shared;

namespace WebWayCMS.Services;

public interface IDefaultContentSeeder
{
    Task SeedDefaultPagesAsync(bool seedAdminPage, CancellationToken ct = default);
}
