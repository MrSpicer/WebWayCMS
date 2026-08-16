using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Models.Article;
using WebWayCMS.Models.ContentBlock;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Security;

namespace WebWayCMS.Core.Tests;

public sealed class ValidatorProbe
{
    [FormProperty(IsRequired = true)]
    public Guid? Optional { get; set; }

    [FormProperty(IsRequired = true)]
    public int Count { get; set; } = 5;
}

[TestFixture]
public class ModelValidatorTests
{
    [Test]
    public void Validate_NullModel_Throws()
    {
        Assert.That(() => ModelValidator.Validate(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void Validate_ValidModel_ReturnsNull()
    {
        var vm = new ContentBlockUpsertViewModel { Title = "T", Content = "c" };

        Assert.That(ModelValidator.Validate(vm), Is.Null);
    }

    [Test]
    public void Validate_EmptyRequiredTitle_ReturnsFailure()
    {
        var vm = new ContentBlockUpsertViewModel { Title = "", Content = "c" };

        var result = ModelValidator.Validate(vm);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("Title"));
        });
    }

    [Test]
    public void Validate_WhitespaceRequiredField_ReturnsFailure()
    {
        var vm = new ContentBlockUpsertViewModel { Title = "T", Content = "   " };

        var result = ModelValidator.Validate(vm);

        Assert.That(result!.Success, Is.False);
        Assert.That(result.ErrorField, Is.EqualTo("Content"));
    }

    [Test]
    public void Validate_FormPropertyOnlyRequired_ReturnsFailureWithLabel()
    {
        var vm = new ContentZoneItemUpsertViewModel { ComponentName = "" };

        var result = ModelValidator.Validate(vm);

        Assert.Multiple(() =>
        {
            Assert.That(result!.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("ComponentName"));
            Assert.That(result.ErrorMessage, Is.EqualTo("Component Name is required."));
        });
    }

    [Test]
    public void Validate_FormPropertyOnlyRequired_NonEmpty_Passes()
    {
        var vm = new ContentZoneItemUpsertViewModel { ComponentName = "ContentBlock" };

        Assert.That(ModelValidator.Validate(vm), Is.Null);
    }

    [Test]
    public void Validate_ReportsPascalCaseErrorField_NotCamelCase()
    {
        var vm = new ContentBlockUpsertViewModel { Title = "T", Content = "" };

        var result = ModelValidator.Validate(vm);

        Assert.That(result!.ErrorField, Is.EqualTo("Content"));
        Assert.That(result.ErrorField, Is.Not.EqualTo("content"));
    }

    [Test]
    public void Validate_NullRequiredValue_FailsWithPropertyNameLabel()
    {
        var vm = new ValidatorProbe { Optional = null };

        var result = ModelValidator.Validate(vm);

        Assert.Multiple(() =>
        {
            Assert.That(result!.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("Optional"));
            Assert.That(result.ErrorMessage, Is.EqualTo("Optional is required."));
        });
    }

    [Test]
    public void Validate_NonNullNonStringRequiredValue_Passes()
    {
        var vm = new ValidatorProbe { Optional = Guid.NewGuid(), Count = 5 };

        Assert.That(ModelValidator.Validate(vm), Is.Null);
    }
}
