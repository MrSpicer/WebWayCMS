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

public sealed class ConstraintProbe
{
    [FormProperty(Min = 1, Max = 10)]
    public int Count { get; set; }

    [FormProperty(MaxLength = 5)]
    public string? Short { get; set; }

    [FormProperty(Pattern = "^[a-z]+$", PatternErrorMessage = "Lowercase only.")]
    public string? Code { get; set; }
}

public sealed class MinOnlyProbe
{
    [FormProperty(Min = 5)]
    public int Count { get; set; } = 7;
}

public sealed class MaxOnlyProbe
{
    [FormProperty(Max = 5)]
    public int Count { get; set; } = 3;
}

public sealed class UnconstrainedProbe
{
    [FormProperty(Pattern = "^[a-z]+$")]
    public string? Anything { get; set; }
}

public sealed class UnanchoredProbe
{
    [FormProperty(Pattern = "[a-z0-9-]+")]
    public string? Slug { get; set; }
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

    [Test]
    public void Validate_BelowMin_ReturnsFailure()
    {
        var vm = new ConstraintProbe { Count = 0 };

        var result = ModelValidator.Validate(vm);

        Assert.Multiple(() =>
        {
            Assert.That(result!.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("Count"));
            Assert.That(result.ErrorMessage, Is.EqualTo("Count must be at least 1."));
        });
    }

    [Test]
    public void Validate_AboveMax_ReturnsFailure()
    {
        var vm = new ConstraintProbe { Count = 11 };

        var result = ModelValidator.Validate(vm);

        Assert.Multiple(() =>
        {
            Assert.That(result!.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("Count"));
            Assert.That(result.ErrorMessage, Is.EqualTo("Count must be at most 10."));
        });
    }

    [Test]
    public void Validate_WithinRange_Passes()
    {
        var vm = new ConstraintProbe { Count = 5 };

        Assert.That(ModelValidator.Validate(vm), Is.Null);
    }

    [Test]
    public void Validate_OverMaxLength_ReturnsFailure()
    {
        var vm = new ConstraintProbe { Count = 5, Short = "too long" };

        var result = ModelValidator.Validate(vm);

        Assert.Multiple(() =>
        {
            Assert.That(result!.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("Short"));
            Assert.That(result.ErrorMessage, Is.EqualTo("Short must not exceed 5 characters."));
        });
    }

    [Test]
    public void Validate_PatternMismatch_UsesThePatternErrorMessage()
    {
        var vm = new ConstraintProbe { Count = 5, Code = "ABC" };

        var result = ModelValidator.Validate(vm);

        Assert.Multiple(() =>
        {
            Assert.That(result!.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("Code"));
            Assert.That(result.ErrorMessage, Is.EqualTo("Lowercase only."));
        });
    }

    [Test]
    public void Validate_PatternMismatch_WithoutMessage_FallsBackToPropertyName()
    {
        var vm = new UnconstrainedProbe { Anything = "ABC" };

        var result = ModelValidator.Validate(vm);

        Assert.Multiple(() =>
        {
            Assert.That(result!.Success, Is.False);
            Assert.That(result.ErrorField, Is.EqualTo("Anything"));
            Assert.That(result.ErrorMessage, Is.EqualTo("Anything has an invalid format."));
        });
    }

    [Test]
    public void Validate_AllConstraintsSatisfied_Passes()
    {
        var vm = new ConstraintProbe { Count = 5, Short = "ok", Code = "abc" };

        Assert.That(ModelValidator.Validate(vm), Is.Null);
    }

    [Test]
    public void Validate_MinOnlyViolation_ReturnsFailure()
    {
        var vm = new MinOnlyProbe { Count = 3 };

        var result = ModelValidator.Validate(vm);

        Assert.Multiple(() =>
        {
            Assert.That(result!.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("Count must be at least 5."));
        });
    }

    [Test]
    public void Validate_MaxOnlyViolation_ReturnsFailure()
    {
        var vm = new MaxOnlyProbe { Count = 9 };

        var result = ModelValidator.Validate(vm);

        Assert.Multiple(() =>
        {
            Assert.That(result!.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("Count must be at most 5."));
        });
    }

    [Test]
    public void Validate_EmptyOptionalPatternField_Passes()
    {
        // HTML pattern semantics: an empty optional field is not format-checked.
        Assert.That(ModelValidator.Validate(new UnconstrainedProbe { Anything = string.Empty }), Is.Null);
    }

    [Test]
    public void Validate_Pattern_MustMatchTheWholeValue()
    {
        var result = ModelValidator.Validate(new UnanchoredProbe { Slug = "Bad Value!-" });

        Assert.Multiple(() =>
        {
            Assert.That(result!.ErrorField, Is.EqualTo("Slug"));
            Assert.That(ModelValidator.Validate(new UnanchoredProbe { Slug = "good-value" }), Is.Null);
        });
    }
}
