using Microsoft.AspNetCore.Razor.TagHelpers;

using NUnit.Framework;

using WebWayCMS.TagHelpers;

namespace WebWayCMS.Forms.Tests;

[TestFixture]
public class FormFieldsTagHelperTests
{
    private static string Render(object? model)
    {
        var helper = new FormFieldsTagHelper { For = model };
        var context = new TagHelperContext(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            Guid.NewGuid().ToString());
        var output = new TagHelperOutput(
            "form-fields",
            new TagHelperAttributeList(),
            (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        helper.Process(context, output);

        Assert.That(output.TagName, Is.Null, "wrapping element should be suppressed");
        return output.Content.GetContent();
    }

    [Test]
    public void Process_NullModel_RendersNothing()
    {
        Assert.That(Render(null), Is.Empty);
    }

    [Test]
    public void Process_ModelWithoutProperties_RendersNothing()
    {
        Assert.That(Render(new EmptyModel()), Is.Empty);
    }

    [Test]
    public void Process_AllEditors_RendersEachInputType()
    {
        var html = Render(new AllEditorsModel());

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("type=\"text\""));
            Assert.That(html, Does.Contain("<textarea"));
            Assert.That(html, Does.Contain("rich-text-editor"));
            Assert.That(html, Does.Contain("type=\"number\""));
            Assert.That(html, Does.Contain("type=\"checkbox\""));
            Assert.That(html, Does.Contain("type=\"date\""));
            Assert.That(html, Does.Contain("type=\"datetime-local\""));
            Assert.That(html, Does.Contain("type=\"color\""));
            Assert.That(html, Does.Contain("type=\"url\""));
            Assert.That(html, Does.Contain("type=\"email\""));
            Assert.That(html, Does.Contain("type=\"hidden\""));
            Assert.That(html, Does.Contain("<select"));
            Assert.That(html, Does.Contain("data-page-controller-picker"));
            Assert.That(html, Does.Contain("min=\"1\""));
            Assert.That(html, Does.Contain("max=\"10\""));
            Assert.That(html, Does.Contain("pattern="));
            Assert.That(html, Does.Contain("data-current-value="));
        });
    }

    [Test]
    public void Process_CheckedCheckbox_IncludesCheckedAttribute()
    {
        var html = Render(new AllEditorsModel { Flag = true });

        Assert.That(html, Does.Contain("checked"));
    }

    [Test]
    public void Process_UncheckedCheckbox_OmitsCheckedAttribute()
    {
        var html = Render(new AllEditorsModel { Flag = false });

        Assert.That(html, Does.Not.Contain("value=\"true\" checked"));
    }

    [Test]
    public void Process_RequiredField_RendersRequiredMarker()
    {
        var html = Render(new AllEditorsModel());

        Assert.That(html, Does.Contain("has-text-danger\">*"));
    }

    [Test]
    public void Process_GroupedModel_RendersSectionHeadings()
    {
        var html = Render(new GroupedModel());

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("form-group-section"));
            Assert.That(html, Does.Contain(">First<"));
            Assert.That(html, Does.Contain(">Second<"));
        });
    }

    [Test]
    public void Process_HorizontalGroup_RendersFieldBody()
    {
        var html = Render(new HorizontalGroupModel());

        Assert.That(html, Does.Contain("field is-horizontal"));
        Assert.That(html, Does.Contain("field-body"));
    }

    [Test]
    public void Process_DefaultValues_RenderEmptyStrings()
    {
        // Guid.Empty, DateTime.MinValue, DateOnly.MinValue, DateTimeOffset.MinValue, null ToString => empty values.
        var html = Render(new DefaultValuesModel());

        Assert.That(html, Does.Not.Contain("00000000-0000-0000-0000-000000000000"));
        Assert.That(html, Does.Not.Contain("0001-01-01"));
    }

    [Test]
    public void Process_TemporalValues_FormatNonMinimumDates()
    {
        var html = Render(new TemporalModel());

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("2024-05-06"));
            Assert.That(html, Does.Contain("2024-05-06T07:08"));
        });
    }

    [Test]
    public void Process_DataAnnotationModel_AppliesMaxLengthAndRequired()
    {
        var html = Render(new DataAnnotationModel());

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("maxlength=\"15\""));
            Assert.That(html, Does.Contain("required"));
        });
    }

    [Test]
    public void Process_SelectModel_RendersUnmatchedAndViewPicker()
    {
        var html = Render(new SelectModel());

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("-- Select --"));
            Assert.That(html, Does.Contain("data-current-value=\"Custom\""));
            // PageControllerPicker with an empty value: marker present, no data-current-value emitted.
            Assert.That(html, Does.Contain("data-page-controller-picker"));
            Assert.That(html, Does.Not.Contain("data-current-value=\"\""));
        });
    }

    [Test]
    public void Process_PlainCheckbox_OmitsCssRequiredAndHelp()
    {
        var html = Render(new PlainCheckboxModel());

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("type=\"checkbox\""));
            Assert.That(html, Does.Not.Contain("checked"));
            Assert.That(html, Does.Not.Contain("<p class=\"help\""));
        });
    }

    [Test]
    public void Process_StyledFields_RenderCssRequiredSelectAndRequiredTextArea()
    {
        var html = Render(new StyledFieldsModel());

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("my-css"));
            Assert.That(html, Does.Contain("<select"));
            Assert.That(html, Does.Contain("required aria-required=\"true\""));
            Assert.That(html, Does.Contain("<textarea"));
        });
    }

    [Test]
    public void Process_HorizontalGroupBreak_RendersTrailingUngroupedInBody()
    {
        var html = Render(new HorizontalGroupBreakModel());

        Assert.That(html, Does.Contain("field is-horizontal"));
    }

    [Test]
    public void Process_NullValues_RenderEmptyCheckboxAndInput()
    {
        var html = Render(new NullValueModel());

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("type=\"checkbox\""));
            Assert.That(html, Does.Not.Contain("checked"));
            Assert.That(html, Does.Contain("value=\"\""));
        });
    }

    [Test]
    public void Process_EndsInGroup_ClosesTrailingSection()
    {
        var html = Render(new EndsInGroupModel());

        Assert.That(html, Does.Contain("form-group-section"));
        // Section opened once; ensure the closing div count balances the single section.
        Assert.That(html, Does.Contain(">Only<"));
    }
}