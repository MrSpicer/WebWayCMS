using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Mapping;
using WebWayCMS.Models.Page;
using WebWayCMS.Pages;
using WebWayCMS.Services;

namespace WebWayCMS.Core.Tests;

/// <summary>
/// Covers defensive guards on internal types that are pre-empted by their public callers.
/// Reachable here via InternalsVisibleTo.
/// </summary>
[TestFixture]
public class CoreInternalsTests
{
	[Test]
	public void Mapper_NullMaps_Throws()
	{
		Assert.That(() => new Mapper(null!), Throws.ArgumentNullException);
	}

	[Test]
	public void PageRegistryHandler_NullViewDiscovery_Throws()
	{
		var registry = Substitute.For<IPageControllerRegistry>();

		Assert.That(() => new PageRegistryHandler(registry, null!), Throws.ArgumentNullException);
	}

	[Test]
	public void PageRegistryHandler_ConstructsWithValidArguments()
	{
		Assert.That(() => new PageRegistryHandler(
			Substitute.For<IPageControllerRegistry>(),
			Substitute.For<IViewDiscoveryService>()), Throws.Nothing);
	}
}
