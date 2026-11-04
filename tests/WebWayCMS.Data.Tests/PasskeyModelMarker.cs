using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;

namespace WebWayCMS.Data.Tests;

internal sealed class PasskeyModelMarker : ICmsModelExtension
{
    public void Configure(ModelBuilder modelBuilder)
    {
    }
}
