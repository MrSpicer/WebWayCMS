using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

/// <summary>
/// NOTE — the InMemory coverage gap: this suite uses <c>UseInMemoryDatabase</c>, which enforces
/// NEITHER filtered unique indexes NOR check constraints, and silently ignores transactions. The
/// three invariant indexes on ContentVersion (UX_ContentVersion_PublishedVariant,
/// UX_ContentVersion_DraftVariant, UX_ContentVersion_Number) and the CK_ContentZoneAssignments_OneParent
/// check constraint are therefore untestable here — InMemory will happily let two published versions
/// coexist and every unit test will pass. The service enforces the invariants in code as the primary
/// mechanism (with the indexes as the DB backstop), and the constraints themselves must be verified
/// against real PostgreSQL via <c>./scripts/StartIntegrationHost.sh</c>, not asserted from unit tests.
/// </summary>
internal sealed class TestReadContext : IContentReadContext
{
    public TestReadContext(ContentReadMode mode) => Mode = mode;

    public ContentReadMode Mode { get; }
    public string Culture => string.Empty;
    public string Segment => string.Empty;
}
