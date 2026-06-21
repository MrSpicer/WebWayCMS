using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers;
using WebWayCMS.Data.Models;
using WebWayCMS.Rendering;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class GenericPageControllerTests
{
	private static GenericPageController BuildGeneric(ICmsPageRenderer renderer)
	{
		var controller = new GenericPageController(renderer);
		controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
		return controller;
	}

	private static GenericAdminPageController BuildAdmin(ICmsPageRenderer renderer)
	{
		var controller = new GenericAdminPageController(renderer);
		controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
		return controller;
	}

	[Test]
	public void Constructor_NullRenderer_Throws()
		=> Assert.That(() => new GenericPageController(null!), Throws.ArgumentNullException);

	[Test]
	public async Task GenericPage_Index_DelegatesToRenderer_WithAndWithoutPageContext()
	{
		var renderer = Substitute.For<ICmsPageRenderer>();
		var expected = new EmptyResult();
		renderer.RenderPage(Arg.Any<PageDTO?>(), Arg.Any<object>()).Returns(expected);
		var controller = BuildGeneric(renderer);

		// No CMS:PageData / CMS:PageConfig -> null page + a fresh config, both forwarded to the renderer.
		var first = await controller.Index();
		Assert.That(first, Is.SameAs(expected));

		// With page data + an existing config -> both forwarded to the renderer.
		var page = new PageDTO { ViewName = "Custom", ContentMeta = new ContentDTO { Id = Guid.NewGuid(), Title = "T" } };
		var config = new GenericPageConfiguration { Style = "x" };
		controller.HttpContext.Items["CMS:PageData"] = page;
		controller.HttpContext.Items["CMS:PageConfig"] = config;
		var second = await controller.Index();
		Assert.That(second, Is.SameAs(expected));

		Assert.Multiple(() =>
		{
			renderer.Received(1).RenderPage(Arg.Is<PageDTO?>(p => p == null), Arg.Is<object>(o => o is GenericPageConfiguration));
			renderer.Received(1).RenderPage(page, config);
		});
	}

	[Test]
	public void AdminConstructor_NullRenderer_Throws()
		=> Assert.That(() => new GenericAdminPageController(null!), Throws.ArgumentNullException);

	[Test]
	public async Task GenericAdminPage_Index_DelegatesToRenderer_ForwardingViewName()
	{
		var renderer = Substitute.For<ICmsPageRenderer>();
		var expected = new EmptyResult();
		renderer.RenderAdminPage(Arg.Any<PageDTO?>(), Arg.Any<object>(), Arg.Any<string?>()).Returns(expected);
		var controller = BuildAdmin(renderer);

		// No page context -> null page, null view name forwarded.
		var first = await controller.Index();
		Assert.That(first, Is.SameAs(expected));

		// With a page whose ViewName selects the dashboard.
		var page = new PageDTO { ViewName = "Dashboard", ContentMeta = new ContentDTO { Id = Guid.NewGuid(), Title = "T" } };
		var config = new GenericPageConfiguration();
		controller.HttpContext.Items["CMS:PageData"] = page;
		controller.HttpContext.Items["CMS:PageConfig"] = config;
		var second = await controller.Index();
		Assert.That(second, Is.SameAs(expected));

		Assert.Multiple(() =>
		{
			renderer.Received(1).RenderAdminPage(Arg.Is<PageDTO?>(p => p == null), Arg.Is<object>(o => o is GenericPageConfiguration), null);
			renderer.Received(1).RenderAdminPage(page, config, "Dashboard");
		});
	}
}

[TestFixture]
public class ErrorControllerTests
{
	private ErrorController Build()
	{
		var controller = new ErrorController();
		controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
		return controller;
	}

	private static ContentResult IndexResult(ErrorController c) => (ContentResult)c.Index();

	[Test]
	public void Index_NoExceptionFeature_ReturnsErrorContent()
	{
		var result = IndexResult(Build());
		Assert.Multiple(() =>
		{
			Assert.That(result.ContentType, Does.Contain("text/html"));
			Assert.That(result.Content, Does.Contain("An error occurred while processing your request."));
		});
	}

	[Test]
	public void Index_WithExceptionFeature_LogsAndReturnsContent()
	{
		var controller = Build();
		controller.HttpContext.Features.Set<IExceptionHandlerPathFeature>(
			new ExceptionHandlerFeature { Error = new InvalidOperationException("x"), Path = "/p" });

		Assert.That(IndexResult(controller).Content, Does.Contain("Error."));
	}

	[Test]
	public void Index_FeatureWithoutError_SkipsLogging()
	{
		var controller = Build();
		controller.HttpContext.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature());

		Assert.That(IndexResult(controller).Content, Does.Contain("Error."));
	}

	[Test]
	public void StatusCodeHandler_ReturnsErrorContent()
	{
		Assert.That(((ContentResult)Build().StatusCodeHandler(404)).Content, Does.Contain("An error occurred"));
	}

	[Test]
	public void Index_WithCurrentActivity_UsesActivityId()
	{
		using var activity = new System.Diagnostics.Activity("test");
		activity.Start();
		try
		{
			Assert.That(IndexResult(Build()).Content, Does.Contain(activity.Id!));
			Assert.That(((ContentResult)Build().StatusCodeHandler(500)).Content, Does.Contain("Request ID"));
		}
		finally
		{
			activity.Stop();
		}
	}
}
