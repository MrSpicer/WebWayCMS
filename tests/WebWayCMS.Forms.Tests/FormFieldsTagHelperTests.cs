using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

using NSubstitute;
using NUnit.Framework;

using WebWayCMS.Forms;
using WebWayCMS.TagHelpers;
using WebWayCMS.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace WebWayCMS.Forms.Tests;

[TestFixture]
public class FormFieldsTagHelperTests
{
    private ViewContext _viewContext = null!;
    private IFormComponentResolver _resolver = null!;
    private IViewComponentHelper _vch = null!;

    [SetUp]
    public void SetUp()
    {
        _resolver = Substitute.For<IFormComponentResolver>();
        _vch = Substitute.For<IViewComponentHelper, IViewContextAware>();
        var services = new ServiceCollection();
        services.AddSingleton(_vch);
        var sp = services.BuildServiceProvider();

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.RequestServices = sp;

        _viewContext = new ViewContext(
            new Microsoft.AspNetCore.Mvc.ActionContext(
                httpContext,
                new Microsoft.AspNetCore.Routing.RouteData(),
                new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),
            Substitute.For<Microsoft.AspNetCore.Mvc.ViewEngines.IView>(),
            new ViewDataDictionary(
                new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),
                new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary()),
            new TempDataDictionary(
                new Microsoft.AspNetCore.Http.DefaultHttpContext(),
                Substitute.For<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>()),
            TextWriter.Null,
            new HtmlHelperOptions());
    }

    private async Task<string> RenderAsync(object? model, FormFieldMode mode = FormFieldMode.Write, FormFieldBinding binding = FormFieldBinding.Model)
    {
        var helper = new FormFieldsTagHelper(_resolver)
        {
            For = model,
            Mode = mode,
            Binding = binding,
            ViewContext = _viewContext
        };

        var context = new TagHelperContext(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            Guid.NewGuid().ToString());
        var output = new TagHelperOutput(
            "form-fields",
            new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        await helper.ProcessAsync(context, output);

        Assert.That(output.TagName, Is.Null, "wrapping element should be suppressed");
        return output.Content.GetContent();
    }

    [Test]
    public async Task Process_NullModel_RendersNothing()
    {
        Assert.That(await RenderAsync(null), Is.Empty);
    }

    [Test]
    public async Task Process_ModelWithoutProperties_RendersNothing()
    {
        Assert.That(await RenderAsync(new EmptyModel()), Is.Empty);
    }

    [Test]
    public async Task Process_GroupedModel_RendersSectionHeadings()
    {
        var html = await RenderAsync(new GroupedModel());

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("form-group-section"));
            Assert.That(html, Does.Contain(">First<"));
            Assert.That(html, Does.Contain(">Second<"));
        });
    }

    [Test]
    public async Task Process_HorizontalGroup_RendersFieldBody()
    {
        var componentInfo = new FormComponentInfo
        {
            Name = "Text",
            ViewComponentName = "Text",
            WriteViewName = "Write",
            ReadViewName = "Read"
        };
        _resolver.Resolve(Arg.Any<FormPropertyInfo>()).Returns(componentInfo);
        _vch.InvokeAsync(componentInfo.ViewComponentName, Arg.Any<object>()).Returns(Task.FromResult<IHtmlContent>(new HtmlString("<mock/>")));

        var html = await RenderAsync(new HorizontalGroupModel());

        Assert.That(html, Does.Contain("field is-horizontal"));
        Assert.That(html, Does.Contain("field-body"));
    }

    [Test]
    public async Task Process_HorizontalGroupBreak_RendersTrailingUngroupedInBody()
    {
        var componentInfo = new FormComponentInfo
        {
            Name = "Text",
            ViewComponentName = "Text",
            WriteViewName = "Write",
            ReadViewName = "Read"
        };
        _resolver.Resolve(Arg.Any<FormPropertyInfo>()).Returns(componentInfo);
        _vch.InvokeAsync(componentInfo.ViewComponentName, Arg.Any<object>()).Returns(Task.FromResult<IHtmlContent>(new HtmlString("<mock/>")));

        var html = await RenderAsync(new HorizontalGroupBreakModel());

        Assert.That(html, Does.Contain("field is-horizontal"));
    }

    [Test]
    public async Task Process_EndsInGroup_ClosesTrailingSection()
    {
        var html = await RenderAsync(new EndsInGroupModel());

        Assert.That(html, Does.Contain("form-group-section"));
        Assert.That(html, Does.Contain(">Only<"));
    }

    [Test]
    public void Constructor_NullResolver_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new FormFieldsTagHelper(null!));
        Assert.That(ex!.ParamName, Is.EqualTo("resolver"));
    }

    [Test]
    public async Task Process_ReadMode_RendersField()
    {
        var componentInfo = new FormComponentInfo
        {
            Name = "Text",
            ViewComponentName = "Text",
            WriteViewName = "Write",
            ReadViewName = "Read"
        };
        _resolver.Resolve(Arg.Any<FormPropertyInfo>()).Returns(componentInfo);
        _vch.InvokeAsync(componentInfo.ViewComponentName, Arg.Any<object>()).Returns(Task.FromResult<IHtmlContent>(new HtmlString("<mock/>")));

        var html = await RenderAsync(new StyledFieldsModel(), mode: FormFieldMode.Read);

        Assert.That(html, Does.Contain("<mock/>"));
    }

    [Test]
    public async Task Process_JsonBinding_RendersField()
    {
        var componentInfo = new FormComponentInfo
        {
            Name = "Text",
            ViewComponentName = "Text",
            WriteViewName = "Write",
            ReadViewName = "Read"
        };
        _resolver.Resolve(Arg.Any<FormPropertyInfo>()).Returns(componentInfo);
        _vch.InvokeAsync(componentInfo.ViewComponentName, Arg.Any<object>()).Returns(Task.FromResult<IHtmlContent>(new HtmlString("<mock/>")));

        var html = await RenderAsync(new StyledFieldsModel(), binding: FormFieldBinding.Json);

        Assert.That(html, Does.Contain("<mock/>"));
    }
}
