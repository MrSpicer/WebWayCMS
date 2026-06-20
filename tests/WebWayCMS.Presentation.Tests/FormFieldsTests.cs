using NUnit.Framework;

using WebWayCMS.Presentation.Components;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class FormFieldsTests
{
	[Test]
	public async Task RendersFieldsFromModelMetadata()
	{
		// GenericPageConfiguration carries [FormProperty] metadata: a text ViewName plus textareas.
		var html = await BlazorRenderHarness.RenderAsync<FormFields>(
			new Dictionary<string, object?> { ["Model"] = new WebWayCMS.Controllers.GenericPageConfiguration() });

		Assert.Multiple(() =>
		{
			Assert.That(html, Does.Contain("name=\"ViewName\""));
			Assert.That(html, Does.Contain("<textarea"));
			Assert.That(html, Does.Contain("class=\"label\""));
		});
	}

	[Test]
	public async Task NullModel_RendersNothing()
	{
		var html = await BlazorRenderHarness.RenderAsync<FormFields>();
		Assert.That(html.Trim(), Is.Empty);
	}
}
