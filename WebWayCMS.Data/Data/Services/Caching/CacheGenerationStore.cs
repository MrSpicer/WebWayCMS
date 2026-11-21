using System.Data;
using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services.Caching;

/// <inheritdoc />
/// <remarks>
/// Not sealed: tests subclass it to swap <see cref="BumpCoreAsync"/> for an InMemory-compatible
/// implementation (the in-memory provider cannot run raw SQL).
/// </remarks>
public class CacheGenerationStore : ICacheGenerationStore
{
    internal const int SingleRowId = 1;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger = Log.ForContext<CacheGenerationStore>();

    public CacheGenerationStore(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));

    public Task<long> ReadAsync(CancellationToken ct = default)
        => new ScopeRunner(_scopeFactory).RunAsync(
            async (context, token) =>
                (await context.Set<CacheGenerationDTO>().FirstOrDefaultAsync(r => r.Id == SingleRowId, token))?.Value ?? 0,
            ct);

    public async Task<(bool Success, long? Value, string? ErrorMessage)> BumpAsync(CancellationToken ct = default)
    {
        try
        {
            var value = await new ScopeRunner(_scopeFactory).RunAsync((context, token) => BumpCoreAsync(context, token), ct);
            return (true, value, null);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to bump the cache generation stamp");
            return (false, null, ex.Message);
        }
    }

    /// <summary>
    /// The one atomic statement, run through ADO (not <c>Database.SqlQuery</c>: EF refuses to compose
    /// over a non-<c>SELECT</c> statement): an upsert that increments <c>"Value"</c> and returns it.
    /// Two concurrent bumps never collide on a value, so every commit moves the stamp exactly once.
    /// When the row does not exist yet it is created at <c>1</c> (not <c>0</c>): reads of a missing
    /// row count as generation <c>0</c>, so a first-ever write must still move the stamp. The bump
    /// always runs post-commit on its own connection (see <c>ScopeRunner</c>), so the statement
    /// auto-commits. Excluded from coverage like the other DB-orchestration code — InMemory cannot
    /// run raw SQL; the integration host verifies it against Postgres.
    /// </summary>
    [ExcludeFromCodeCoverage]
    protected virtual async Task<long> BumpCoreAsync(CmsDbContext context, CancellationToken ct)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"INSERT INTO \"CacheGenerations\" (\"Id\", \"Value\") VALUES ({SingleRowId}, 1) ON CONFLICT (\"Id\") DO UPDATE SET \"Value\" = \"CacheGenerations\".\"Value\" + 1 RETURNING \"Value\"";
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }
}
