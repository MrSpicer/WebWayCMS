using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Routing;

using NSubstitute;
using NUnit.Framework;

using WebWayCMS.Forms;
using WebWayCMS.TagHelpers;

namespace WebWayCMS.Forms.Tests;

[TestFixture]
public class FormFieldTagHelperTests
{
    private static FormFieldContext CreateField(
        string name = "TestField",
        string helpText = "",
        bool isRequired = false,
        string cssClass = "",
        object? value = null,
        string elementId = "TestField")
    {
        var prop = new FormPropertyInfo
        {
            Name = name,
            Label = name,
            HelpText = helpText,
            IsRequired = isRequired,
            CssClass = cssClass,
            PropertyType = typeof(string),
        };
        return new FormFieldContext
        {
            Property = prop,
            Value = value,
            InputName = name,
            ElementId = elementId,
        };
    }

    private static ViewContext CreateViewContext()
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary());
        return new ViewContext(
            actionContext,
            Substitute.For<IView>(),
            viewData,
            new TempDataDictionary(httpContext, Substitute.For<ITempDataProvider>()),
            TextWriter.Null,
            new HtmlHelperOptions());
    }

    private async Task<string> RenderAsync(FormFieldContext? field, string chrome = "", string childContent = "<input/>", ViewContext? viewContext = null)
    {
        var helper = new FormFieldTagHelper
        {
            Field = field,
            Chrome = chrome,
            ViewContext = viewContext ?? CreateViewContext()
        };

        var context = new TagHelperContext(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            Guid.NewGuid().ToString());

        var output = new TagHelperOutput(
            "form-field",
            new TagHelperAttributeList(),
            (_, _) =>
            {
                var content = new DefaultTagHelperContent();
                content.SetHtmlContent(childContent);
                return Task.FromResult<TagHelperContent>(content);
            });

        await helper.ProcessAsync(context, output);
        return output.Content.GetContent();
    }

    [Test]
    public async Task Process_NullField_SuppressesOutput()
    {
        var helper = new FormFieldTagHelper { Field = null };

        var context = new TagHelperContext(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            Guid.NewGuid().ToString());
        var output = new TagHelperOutput(
            "form-field",
            new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        await helper.ProcessAsync(context, output);

        Assert.That(output.Content.IsEmptyOrWhiteSpace, Is.True);
    }

    [Test]
    public async Task Process_ChromeNone_RendersChildContentOnly()
    {
        var field = CreateField();
        var result = await RenderAsync(field, chrome: "none", childContent: "<span>test</span>");

        Assert.That(result, Is.EqualTo("<span>test</span>"));
    }

    [Test]
    public async Task Process_ChromeNone_CaseInsensitive()
    {
        var field = CreateField();
        var result = await RenderAsync(field, chrome: "NONE", childContent: "<input/>");

        Assert.That(result, Does.Not.Contain("class=\"field\""));
    }

    [Test]
    public async Task Process_DefaultChrome_RendersFullStructure()
    {
        var field = CreateField();
        var result = await RenderAsync(field, childContent: "<input/>");

        var encodedName = HtmlEncoder.Default.Encode(field.Name);
        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("<div class=\"field\">"));
            Assert.That(result, Does.Contain($"<label class=\"label\" for=\"{encodedName}\">"));
            Assert.That(result, Does.Contain("<div class=\"control\">"));
            Assert.That(result, Does.Contain("<input/>"));
            Assert.That(result, Does.Contain("</div>"));
            Assert.That(result, Does.Contain($"<span role=\"alert\" data-valmsg-for=\"{encodedName}\" class=\"has-text-danger\">"));
            Assert.That(result, Does.Contain("</div>"));
        });
    }

    [Test]
    public async Task Process_WithHelpText_RendersHelpParagraph()
    {
        var field = CreateField(helpText: "Enter your name");
        var result = await RenderAsync(field);

        var encodedName = HtmlEncoder.Default.Encode("TestField");
        Assert.That(result, Does.Contain($"<p class=\"help\" id=\"{encodedName}_help\">"));
        Assert.That(result, Does.Contain(HtmlEncoder.Default.Encode("Enter your name")));
        Assert.That(result, Does.Contain("</p>"));
    }

    [Test]
    public async Task Process_WithRequired_RendersRequiredMarker()
    {
        var field = CreateField(isRequired: true);
        var result = await RenderAsync(field);

        Assert.That(result, Does.Contain("<span class=\"has-text-danger\">*</span>"));
    }

    [Test]
    public async Task Process_NotRequired_NoRequiredMarker()
    {
        var field = CreateField(isRequired: false);
        var result = await RenderAsync(field);

        Assert.That(result, Does.Not.Contain("class=\"has-text-danger\">*</span>"));
    }

    [Test]
    public async Task Process_WithCssClass_AppendsClassToFieldDiv()
    {
        var field = CreateField(cssClass: "is-grouped");
        var result = await RenderAsync(field);

        Assert.That(result, Does.Contain($"<div class=\"field {HtmlEncoder.Default.Encode("is-grouped")}\">"));
    }

    [Test]
    public async Task Process_WithoutCssClass_FieldDivHasNoExtraClass()
    {
        var field = CreateField(cssClass: "");
        var result = await RenderAsync(field);

        Assert.That(result, Does.Contain("<div class=\"field\">"));
    }

    [Test]
    public async Task Process_LabelForUsesFieldName()
    {
        var field = CreateField(name: "UserName");
        var result = await RenderAsync(field);

        var encodedName = HtmlEncoder.Default.Encode("UserName");
        Assert.That(result, Does.Contain($"for=\"{encodedName}\""));
    }

    [Test]
    public async Task Process_EncodesSpecialCharactersInLabel()
    {
        var field = CreateField(name: "Has<Special&Chars");
        var result = await RenderAsync(field);

        var encodedName = HtmlEncoder.Default.Encode("Has<Special&Chars");
        Assert.That(result, Does.Contain($"for=\"{encodedName}\""));
        Assert.That(result, Does.Contain($">{encodedName}<"));
    }

    [Test]
    public async Task Process_ValidationSpanUsesFieldName()
    {
        var field = CreateField(name: "Email");
        var result = await RenderAsync(field);

        var encodedName = HtmlEncoder.Default.Encode("Email");
        Assert.That(result, Does.Contain($"<span role=\"alert\" data-valmsg-for=\"{encodedName}\" class=\"has-text-danger\">"));
    }

    [Test]
    public async Task Process_WithModelStateError_DisplaysErrorMessage()
    {
        var field = CreateField(name: "Title");
        var viewContext = CreateViewContext();
        viewContext.ModelState.AddModelError("Title", "The Title field is required.");

        var result = await RenderAsync(field, viewContext: viewContext);

        Assert.That(result, Does.Contain(HtmlEncoder.Default.Encode("The Title field is required.")));
    }

    [Test]
    public async Task Process_NoModelStateError_EmptySpan()
    {
        var field = CreateField(name: "Slug");
        var viewContext = CreateViewContext();

        var result = await RenderAsync(field, viewContext: viewContext);

        var encodedName = HtmlEncoder.Default.Encode("Slug");
        Assert.That(result, Does.Contain($"<span role=\"alert\" data-valmsg-for=\"{encodedName}\" class=\"has-text-danger\"></span>"));
    }

    [Test]
    public async Task Process_NullViewContext_DoesNotThrow()
    {
        var field = CreateField(name: "Field1");
        var helper = new FormFieldTagHelper
        {
            Field = field,
            ViewContext = null!
        };

        var context = new TagHelperContext(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            Guid.NewGuid().ToString());
        var output = new TagHelperOutput(
            "form-field",
            new TagHelperAttributeList(),
            (_, _) =>
            {
                var content = new DefaultTagHelperContent();
                content.SetHtmlContent("<input/>");
                return Task.FromResult<TagHelperContent>(content);
            });

        Assert.DoesNotThrowAsync(async () => await helper.ProcessAsync(context, output));
    }

    [Test]
    public async Task Process_ModelStateError_NoErrorMessage_UsesEmpty()
    {
        var field = CreateField(name: "Field");
        var viewContext = CreateViewContext();
        viewContext.ModelState.AddModelError("Field", "");

        var result = await RenderAsync(field, viewContext: viewContext);

        var encodedName = HtmlEncoder.Default.Encode("Field");
        Assert.That(result, Does.Contain($"<span role=\"alert\" data-valmsg-for=\"{encodedName}\" class=\"has-text-danger\"></span>"));
    }

    [Test]
    public async Task Process_ModelStateError_EncodesMessage()
    {
        var field = CreateField(name: "Field");
        var viewContext = CreateViewContext();
        viewContext.ModelState.AddModelError("Field", "Bad <script>alert('xss')</script> value");

        var result = await RenderAsync(field, viewContext: viewContext);

        var encoded = HtmlEncoder.Default.Encode("Bad <script>alert('xss')</script> value");
        Assert.That(result, Does.Contain(encoded));
        Assert.That(result, Does.Not.Contain("<script>"));
    }

    [Test]
    public async Task Process_ModelStateError_WrongField_EmptySpan()
    {
        var field = CreateField(name: "Title");
        var viewContext = CreateViewContext();
        viewContext.ModelState.AddModelError("OtherField", "An error on another field.");

        var result = await RenderAsync(field, viewContext: viewContext);

        Assert.That(result, Does.Not.Contain("An error on another field."));
    }

    [Test]
    public async Task Process_ModelStateEntryExists_NoErrors_EmptySpan()
    {
        var field = CreateField(name: "Title");
        var viewContext = CreateViewContext();
        viewContext.ModelState.SetModelValue("Title", "some value", "some value");

        var result = await RenderAsync(field, viewContext: viewContext);

        var encodedName = HtmlEncoder.Default.Encode("Title");
        Assert.That(result, Does.Contain($"<span role=\"alert\" data-valmsg-for=\"{encodedName}\" class=\"has-text-danger\"></span>"));
    }
}
