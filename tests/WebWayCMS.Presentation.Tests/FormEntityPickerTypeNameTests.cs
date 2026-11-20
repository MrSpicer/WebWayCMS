using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.ContentTypes;
using WebWayCMS.Forms;
using WebWayCMS.ViewComponents.Forms;

namespace WebWayCMS.Presentation.Tests;

/// <summary>The picker names its target type ("New Article List") from the type's content type registration.</summary>
[TestFixture]
public class FormEntityPickerTypeNameTests
{
    private static FormEntityPicker Invoke(string entityType, IContentTypeRegistrationService? registrations)
    {
        var handler = Substitute.For<IAdminCrudHandler>();
        handler.ContentType.Returns("articlelists");
        handler.DisplayName.Returns("Article List");
        var registry = Substitute.For<IAdminHandlerRegistry>();
        registry.GetHandler(Arg.Any<string>()).Returns((IAdminCrudHandler?)null);
        registry.GetHandler("articlelists").Returns(handler);

        var component = new FormEntityPicker();
        FormComponentHarness.Attach(component, configure: services =>
        {
            services.AddSingleton(registry);
            services.AddSingleton(Substitute.For<ICompositeViewEngine>());
            if (registrations != null)
                services.AddSingleton(registrations);
        });

        component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "ListId", Label = "List", EntityType = entityType },
            Mode = FormFieldMode.Write,
            InputName = "ListId",
            ElementId = "ListId",
        });
        return component;
    }

    [Test]
    public void Invoke_WithARegistration_UsesItsNames()
    {
        var registrations = Substitute.For<IContentTypeRegistrationService>();
        registrations.GetPublishedByKey().Returns(new Dictionary<string, ContentTypeRegistrationDTO>(StringComparer.OrdinalIgnoreCase)
        {
            ["articlelists"] = new() { ContentTypeKey = "articlelists", DisplayName = "Reading List", PluralDisplayName = "Reading Lists" },
        });

        var component = Invoke("articlelists", registrations);

        Assert.Multiple(() =>
        {
            Assert.That(component.ViewData[FormEntityPicker.TypeNameViewDataKey], Is.EqualTo("Reading List"));
            Assert.That(component.ViewData[FormEntityPicker.TypePluralViewDataKey], Is.EqualTo("Reading Lists"));
        });
    }

    [Test]
    public void Invoke_WithoutTheRegistrationService_UsesTheHandlerName()
    {
        var component = Invoke("articlelists", registrations: null);

        Assert.Multiple(() =>
        {
            Assert.That(component.ViewData[FormEntityPicker.TypeNameViewDataKey], Is.EqualTo("Article List"));
            Assert.That(component.ViewData[FormEntityPicker.TypePluralViewDataKey], Is.EqualTo("Article Lists"));
        });
    }

    [Test]
    public void Invoke_UnknownEntityType_SetsNoNames()
    {
        var component = Invoke("nothing", Substitute.For<IContentTypeRegistrationService>());

        Assert.Multiple(() =>
        {
            Assert.That(component.ViewData.ContainsKey(FormEntityPicker.TypeNameViewDataKey), Is.False);
            Assert.That(component.ViewData.ContainsKey(FormEntityPicker.TypePluralViewDataKey), Is.False);
        });
    }
}
