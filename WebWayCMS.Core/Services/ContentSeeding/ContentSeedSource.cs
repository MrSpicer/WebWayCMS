using System.Reflection;

namespace WebWayCMS.Services.ContentSeeding;

/// <summary>A named source of raw JSON content seed text.</summary>
/// <param name="Name">
/// The full file path for a disk source, or the manifest resource name for an embedded one.
/// </param>
/// <param name="Json">The raw JSON text.</param>
/// <param name="Assembly">
/// The declaring assembly for an embedded source; <c>null</c> for a disk source. Carried so that a
/// <c>@media:</c> token can resolve a companion image shipped alongside the seed file.
/// </param>
public record ContentSeedSource(string Name, string Json, Assembly? Assembly = null);
