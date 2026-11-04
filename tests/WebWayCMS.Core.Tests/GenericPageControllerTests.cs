using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using NUnit.Framework;

using WebWayCMS.Controllers;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class GenericPageControllerTests
{
    private static T Build<T>() where T : Controller, new()
    {
        var controller = new T();
        var http = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static PageDTO Page(string? viewName, string title = "T")
        => new()
        {
            ViewName = viewName,
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = Guid.NewGuid() },
                Title = title
            }
        };

    [Test]
    public async Task GenericPage_Index_DefaultView_CustomView_AndConfigFallback()
    {
        var controller = Build<GenericPageController>();
        // No CMS:PageData / CMS:PageConfig -> default view + new config.
        var def = (ViewResult)await controller.Index();
        Assert.That(def.ViewName, Is.Null);
        Assert.That(def.Model, Is.InstanceOf<GenericPageConfiguration>());

        // With a page that specifies a view name and an existing config.
        controller.HttpContext.Items["CMS:PageData"] = Page("Custom");
        controller.HttpContext.Items["CMS:PageConfig"] = new GenericPageConfiguration { Style = "x" };
        var custom = (ViewResult)await controller.Index();
        Assert.That(custom.ViewName, Is.EqualTo("Custom"));

        // A page with the "Default" view name falls back to the default view.
        controller.HttpContext.Items["CMS:PageData"] = Page("Default");
        var defaultNamed = (ViewResult)await controller.Index();
        Assert.That(defaultNamed.ViewName, Is.Null);
    }

    [Test]
    public async Task GenericAdminPage_Index_DefaultAndCustomView()
    {
        var controller = Build<GenericAdminPageController>();
        Assert.That(((ViewResult)await controller.Index()).ViewName, Is.Null);

        controller.HttpContext.Items["CMS:PageData"] = Page("AdminView");
        Assert.That(((ViewResult)await controller.Index()).ViewName, Is.EqualTo("AdminView"));
    }
}
