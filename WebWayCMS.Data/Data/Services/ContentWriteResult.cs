using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

public readonly record struct ContentWriteResult(
    bool Success,
    string? ErrorMessage = null,
    Guid VersionId = default,
    Guid NodeId = default);
