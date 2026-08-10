using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;

namespace WebWayCMS.TestHost.Controllers;

[CmsRoute("/code-test", Order = 0)]
[CmsRoute("/code-test/simple", Order = 10, Action = "Simple")]
[CmsRoute("/code-test/hello/{name}", Order = 20, Action = "HelloName")]
[CmsRoute("/code-test/product/{id:int}", Order = 30, Action = "ProductId")]
[CmsRoute("/code-test/search/{query?}", Order = 40, Action = "Search")]
[CmsRoute("/code-test/item/{id:guid}", Order = 50, Action = "ItemId")]
[CmsRoute("/code-test/page-{slug}", Order = 60, Action = "PageSlug")]
[CmsRoute("/code-test/docs/{**path}", Order = 70, Action = "DocsPath")]
[CmsRoute("/code-test/multi/a", Order = 80, Action = "MultiA")]
[CmsRoute("/code-test/multi/b", Order = 81, Action = "MultiB")]
[CmsRoute("/code-test/custom-action", Order = 90, Action = "Custom")]
[CmsRoute("/code-test/constraints/regex/{slug:regex(^[a-z]+$)}", Order = 100, Action = "RegexSlug")]
public class CodeTestController : Controller
{
    public IActionResult Index()
    {
        ViewData["Title"] = "CmsRoute Tests";
        return View();
    }

    public IActionResult Simple()
    {
        ViewData["Title"] = "Simple Route";
        return View();
    }

    public IActionResult HelloName(string name)
    {
        ViewData["Title"] = "Hello {name}";
        ViewData["Pattern"] = "/code-test/hello/{name}";
        ViewData["ParamName"] = name;
        return View();
    }

    public IActionResult ProductId(int id)
    {
        ViewData["Title"] = "Product {id:int}";
        ViewData["Pattern"] = "/code-test/product/{id:int}";
        ViewData["ParamValue"] = id;
        return View();
    }

    public IActionResult Search(string? query)
    {
        ViewData["Title"] = "Search {query?}";
        ViewData["Pattern"] = "/code-test/search/{query?}";
        ViewData["ParamValue"] = query;
        return View();
    }

    public IActionResult ItemId(Guid id)
    {
        ViewData["Title"] = "Item {id:guid}";
        ViewData["Pattern"] = "/code-test/item/{id:guid}";
        ViewData["ParamValue"] = id;
        return View();
    }

    public IActionResult PageSlug(string slug)
    {
        ViewData["Title"] = "Page-{slug}";
        ViewData["Pattern"] = "/code-test/page-{slug}";
        ViewData["ParamValue"] = slug;
        return View();
    }

    public IActionResult DocsPath(string? path)
    {
        ViewData["Title"] = "Docs {**path}";
        ViewData["Pattern"] = "/code-test/docs/{**path}";
        ViewData["ParamValue"] = path ?? "(no path)";
        return View();
    }

    public IActionResult MultiA()
    {
        ViewData["Title"] = "Multi A";
        ViewData["Pattern"] = "/code-test/multi/a";
        return View();
    }

    public IActionResult MultiB()
    {
        ViewData["Title"] = "Multi B";
        ViewData["Pattern"] = "/code-test/multi/b";
        return View();
    }

    [ActionName("Custom")]
    public IActionResult CustomAction()
    {
        ViewData["Title"] = "Custom Action";
        ViewData["Pattern"] = "/code-test/custom-action (Action = \"Custom\")";
        return View("CustomAction");
    }

    public IActionResult RegexSlug(string slug)
    {
        ViewData["Title"] = "Regex {slug:regex}";
        ViewData["Pattern"] = "/code-test/constraints/regex/{slug:regex(^[a-z]+$)}";
        ViewData["ParamValue"] = slug;
        return View();
    }
}
