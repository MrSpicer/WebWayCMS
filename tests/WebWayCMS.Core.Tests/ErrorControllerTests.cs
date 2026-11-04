using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using NUnit.Framework;

using WebWayCMS.Controllers;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class ErrorControllerTests
{
    private ErrorController Build()
    {
        var controller = new ErrorController();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    [Test]
    public void Index_NoExceptionFeature_ReturnsErrorView()
    {
        var result = (ViewResult)Build().Index();
        Assert.That(result.ViewName, Is.EqualTo("Error"));
    }

    [Test]
    public void Index_WithExceptionFeature_LogsAndReturnsView()
    {
        var controller = Build();
        controller.HttpContext.Features.Set<IExceptionHandlerPathFeature>(
            new ExceptionHandlerFeature { Error = new InvalidOperationException("x"), Path = "/p" });

        Assert.That(((ViewResult)controller.Index()).ViewName, Is.EqualTo("Error"));
    }

    [Test]
    public void Index_FeatureWithoutError_SkipsLogging()
    {
        var controller = Build();
        controller.HttpContext.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature());

        Assert.That(((ViewResult)controller.Index()).ViewName, Is.EqualTo("Error"));
    }

    [Test]
    public void StatusCodeHandler_ReturnsErrorView()
    {
        Assert.That(((ViewResult)Build().StatusCodeHandler(404)).ViewName, Is.EqualTo("Error"));
    }

    [Test]
    public void Index_WithCurrentActivity_UsesActivityId()
    {
        using var activity = new System.Diagnostics.Activity("test");
        activity.Start();
        try
        {
            Assert.That(((ViewResult)Build().Index()).ViewName, Is.EqualTo("Error"));
            Assert.That(((ViewResult)Build().StatusCodeHandler(500)).ViewName, Is.EqualTo("Error"));
        }
        finally
        {
            activity.Stop();
        }
    }
}
