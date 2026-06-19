using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using WebWayCMS.Data.DbContexts;

namespace WebWayCMS.Data.DesignTime;

// Design-time only: lets `dotnet ef` build the model without a host app. The connection string is a
// placeholder — `migrations add` never opens a connection. Real connection strings come from the
// host's configuration at runtime (see WebWayCMS.ServiceCollectionExtensions).
[ExcludeFromCodeCoverage]
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // IdentityDbContext.OnModelCreating reads IdentityOptions.Stores.MaxLengthForKeys from the
        // application service provider to size the Identity key columns (e.g. AspNetUserLogins.LoginProvider,
        // AspNetUserTokens.Name). At runtime AddEntityFrameworkStores sets that to 128; without the same
        // registration here the design-time model leaves those columns as unbounded text, so the generated
        // migration would not match the runtime model and Migrate() trips PendingModelChangesWarning at
        // startup. Mirror the host's Identity store registration so the snapshot matches runtime.
        var appServices = new ServiceCollection()
            .Configure<IdentityOptions>(o => o.Stores.MaxLengthForKeys = 128)
            .BuildServiceProvider();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=webwaycms_designtime;Username=postgres;Password=postgres",
                b => b.MigrationsHistoryTable("__EFMigrationsHistory_Application"))
            .UseApplicationServiceProvider(appServices)
            .Options;
        return new ApplicationDbContext(options);
    }
}
