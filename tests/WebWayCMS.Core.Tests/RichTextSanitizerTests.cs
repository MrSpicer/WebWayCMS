using NUnit.Framework;

using WebWayCMS.Models.Article;
using WebWayCMS.Models.Page;
using WebWayCMS.Security;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class RichTextSanitizerTests
{
    [Test]
    public void Sanitize_NullModel_Throws()
    {
        Assert.That(() => RichTextSanitizer.Sanitize(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void Sanitize_StripsScriptFromRichTextField()
    {
        var vm = new ArticleUpsertViewModel
        {
            Body = "<p>hello</p><script>alert('xss')</script>",
        };

        RichTextSanitizer.Sanitize(vm);

        Assert.That(vm.Body, Does.Contain("<p>hello</p>"));
        Assert.That(vm.Body, Does.Not.Contain("<script"));
    }

    [Test]
    public void Sanitize_LeavesNonRichTextFieldsUntouched()
    {
        var vm = new ArticleUpsertViewModel
        {
            Body = "<b>ok</b>",
            Summary = "<script>bad</script>",
            AuthorName = "<img src=x onerror=alert(1)>",
        };

        RichTextSanitizer.Sanitize(vm);

        // Summary (TextArea) and AuthorName (Text) are not rich-text, so they are not sanitized.
        Assert.That(vm.Summary, Is.EqualTo("<script>bad</script>"));
        Assert.That(vm.AuthorName, Is.EqualTo("<img src=x onerror=alert(1)>"));
    }

    [Test]
    public void Sanitize_EmptyRichTextField_LeftUnchanged()
    {
        var vm = new ArticleUpsertViewModel { Body = string.Empty };

        RichTextSanitizer.Sanitize(vm);

        Assert.That(vm.Body, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Sanitize_NullRichTextValue_DoesNotThrow()
    {
        var vm = new ArticleUpsertViewModel { Body = null! };

        Assert.That(() => RichTextSanitizer.Sanitize(vm), Throws.Nothing);
        Assert.That(vm.Body, Is.Null);
    }

    [Test]
    public void Sanitize_ModelWithNoRichTextFields_IsNoOp()
    {
        var vm = new PageUpsertViewModel { Title = "Home" };

        Assert.That(() => RichTextSanitizer.Sanitize(vm), Throws.Nothing);
        Assert.That(vm.Title, Is.EqualTo("Home"));
    }

    [Test]
    public void SanitizeHtml_RemovesEventHandlersAndJavascriptUris()
    {
        var result = RichTextSanitizer.SanitizeHtml(
            "<a href=\"javascript:alert(1)\" onclick=\"steal()\">link</a><p style=\"color:red\">ok</p>");

        Assert.That(result, Does.Not.Contain("javascript:"));
        Assert.That(result, Does.Not.Contain("onclick"));
        Assert.That(result, Does.Contain("link"));
        Assert.That(result, Does.Contain("ok"));
    }
}
