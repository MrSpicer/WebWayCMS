using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using WebWayCMS.Data.DbContexts;

namespace WebWayCMS.Data.DesignTime;

// Design-time only: lets `dotnet ef` build the model without a host app. The connection string is a
// placeholder — `migrations add` never opens a connection. Real connection strings come from the
// host's configuration at runtime (see WebWayCMS.ServiceCollectionExtensions).
[ExcludeFromCodeCoverage]
public sealed class ContentBlockContextFactory : IDesignTimeDbContextFactory<ContentBlockContext>
{
    public ContentBlockContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ContentBlockContext>()
            .UseNpgsql(
                "Host=localhost;Database=webwaycms_designtime;Username=postgres;Password=postgres",
                b => b.MigrationsHistoryTable("__EFMigrationsHistory_ContentBlock"))
            .Options;
        return new ContentBlockContext(options);
    }
}
