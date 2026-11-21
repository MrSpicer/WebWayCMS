using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;

namespace WebWayCMS.Data.Tests;

/// <summary>
/// A <see cref="CmsDbContext"/> that reports an open transaction (the InMemory provider never does), so
/// read paths that must bypass the content read cache inside a transaction can be tested.
/// </summary>
internal sealed class InTransactionCmsDbContext(DbContextOptions<CmsDbContext> options) : CmsDbContext(options)
{
    public override bool HasOpenTransaction => true;
}
