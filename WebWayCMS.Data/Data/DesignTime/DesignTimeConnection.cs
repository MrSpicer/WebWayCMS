using System.Diagnostics.CodeAnalysis;

namespace WebWayCMS.Data.DesignTime;

// Design-time only: shared placeholder connection string for the IDesignTimeDbContextFactory
// implementations. `dotnet ef` never opens this connection, so the value is inert; it can be
// overridden with the WEBWAYCMS_DESIGNTIME_CONNECTION environment variable (e.g. to scaffold against
// a real database) rather than being hardcoded.
[ExcludeFromCodeCoverage]
internal static class DesignTimeConnection
{
    public static string String =>
        Environment.GetEnvironmentVariable("WEBWAYCMS_DESIGNTIME_CONNECTION")
        ?? "Host=localhost;Database=webwaycms_designtime;Username=postgres;Password=postgres";
}
