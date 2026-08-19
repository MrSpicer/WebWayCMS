namespace WebWayCMS.Services.ContentSeeding;

/// <summary>Applies discovered JSON content seed files to the database at startup.</summary>
public interface IJsonContentSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}
