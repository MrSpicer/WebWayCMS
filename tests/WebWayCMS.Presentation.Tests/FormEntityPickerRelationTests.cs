using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Forms;
using WebWayCMS.ViewComponents.Forms;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class FormEntityPickerRelationTests
{
    private static FormFieldContext Context(bool many = false) => new()
    {
        Property = new FormPropertyInfo
        {
            Name = "FaqId",
            Label = "FAQ",
            EntityType = "faqs",
            IsMultiple = many
        },
        Value = many ? new[] { Guid.NewGuid() } : Guid.NewGuid(),
        Mode = FormFieldMode.Write,
        InputName = "FaqId",
        ElementId = "FaqId"
    };

    private static (FormEntityPicker Picker, IAdminCrudHandler Handler) Create(string[]? writeRoles)
    {
        var handler = Substitute.For<IAdminCrudHandler>();
        handler.ContentType.Returns("faqs");
        handler.WriteRoles.Returns(writeRoles);

        var registry = Substitute.For<IAdminHandlerRegistry>();
        registry.GetHandler("faqs").Returns(handler);

        var picker = new FormEntityPicker();
        FormComponentHarness.Attach(picker, configure: services =>
        {
            services.AddSingleton(registry);
            services.AddSingleton(Substitute.For<ICompositeViewEngine>());
        });
        return (picker, handler);
    }

    private static void As(FormEntityPicker picker, params string[] roles)
    {
        var identity = new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "Test");
        picker.ViewComponentContext!.ViewContext.HttpContext.User = new ClaimsPrincipal(identity);
    }

    [Test]
    public void Invoke_AdminWithNoWriteRoles_CanWriteAndExposesTheContentType()
    {
        var (picker, _) = Create(writeRoles: null);
        As(picker, "Admin");

        picker.Invoke(Context());

        Assert.Multiple(() =>
        {
            Assert.That(picker.ViewData[FormEntityPicker.ContentTypeViewDataKey], Is.EqualTo("faqs"));
            Assert.That(picker.ViewData[FormEntityPicker.CanWriteViewDataKey], Is.True);
            Assert.That(picker.ViewData[FormEntityPicker.EndpointViewDataKey], Is.EqualTo("/wadmin/faqs/api/list"));
        });
    }

    [Test]
    public void Invoke_ExplicitWriteRoles_RequireAMatchingRole()
    {
        var (picker, _) = Create(["Editor"]);
        As(picker, "Admin");
        picker.Invoke(Context());
        var adminCanWrite = picker.ViewData[FormEntityPicker.CanWriteViewDataKey];

        As(picker, "Editor");
        picker.Invoke(Context());
        var editorCanWrite = picker.ViewData[FormEntityPicker.CanWriteViewDataKey];

        Assert.Multiple(() =>
        {
            Assert.That(adminCanWrite, Is.False);
            Assert.That(editorCanWrite, Is.True);
        });
    }

    [Test]
    public void Invoke_UnknownTarget_CannotWriteAndHasNoContentType()
    {
        var picker = new FormEntityPicker();
        FormComponentHarness.Attach(picker);
        As(picker, "Admin");

        picker.Invoke(Context());

        Assert.Multiple(() =>
        {
            Assert.That(picker.ViewData[FormEntityPicker.ContentTypeViewDataKey], Is.Null);
            Assert.That(picker.ViewData[FormEntityPicker.CanWriteViewDataKey], Is.False);
        });
    }

    [Test]
    public void Invoke_ManyField_UsesTheSamePicker()
    {
        var (picker, _) = Create(writeRoles: null);
        As(picker, "Admin");

        var context = Context(many: true);
        var result = picker.Invoke(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.IsMultiple, Is.True);
            Assert.That(Context().IsMultiple, Is.False);
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.default"));
        });
    }
}
