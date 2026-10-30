using NUnit.Framework;

namespace WebWayCMS.Forms.Tests;

[TestFixture]
public class FormPropertyConstraintsTests
{
    private static IReadOnlyList<string> Check(
        object? value, double? min = null, double? max = null, int? maxLength = null,
        string? pattern = null, string? patternErrorMessage = null)
        => FormPropertyConstraints.Check("Field", value, min, max, maxLength, pattern, patternErrorMessage);

    [Test]
    public void Check_NullProperty_Throws()
    {
        Assert.That(() => FormPropertyConstraints.Check(null!, "x"), Throws.ArgumentNullException);
    }

    [Test]
    public void Check_NullValue_HasNoErrors()
    {
        Assert.That(Check(null, min: 1, max: 2, maxLength: 1, pattern: "^a$"), Is.Empty);
    }

    [Test]
    public void Check_NumberOutsideRange_ReportsMinAndMax()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Check(0, min: 1), Is.EqualTo(new[] { "Field must be at least 1." }));
            Assert.That(Check(11, max: 10), Is.EqualTo(new[] { "Field must be at most 10." }));
            Assert.That(Check(5, min: 1, max: 10), Is.Empty);
        });
    }

    [Test]
    public void Check_NonNumericValueWithRange_IsNotRangeChecked()
    {
        Assert.That(Check("abc", min: 1, max: 2), Is.Empty);
    }

    [Test]
    public void Check_NonStringValue_SkipsLengthAndPattern()
    {
        Assert.That(Check(12345, maxLength: 1, pattern: "^a$"), Is.Empty);
    }

    [Test]
    public void Check_StringTooLong_ReportsMaxLength()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Check("abcdef", maxLength: 5), Is.EqualTo(new[] { "Field must not exceed 5 characters." }));
            Assert.That(Check("abcde", maxLength: 5), Is.Empty);
        });
    }

    [Test]
    public void Check_PatternMismatch_UsesTheMessageOrTheDefault()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Check("ABC", pattern: "[a-z]+", patternErrorMessage: "Lowercase only."),
                Is.EqualTo(new[] { "Lowercase only." }));
            Assert.That(Check("ABC", pattern: "[a-z]+"), Is.EqualTo(new[] { "Field has an invalid format." }));
            Assert.That(Check("abc", pattern: "[a-z]+"), Is.Empty);
        });
    }

    [Test]
    public void Check_EmptyString_PassesThePattern()
    {
        // HTML pattern semantics: an empty optional field is not format-checked (required covers it).
        Assert.That(Check(string.Empty, pattern: "^[a-z]+$"), Is.Empty);
    }

    [Test]
    public void Check_UnanchoredPattern_MustMatchTheWholeValue()
    {
        // As the browser applies the form's pattern attribute: "[a-z0-9-]+" means ^(?:[a-z0-9-]+)$.
        Assert.Multiple(() =>
        {
            Assert.That(Check("Bad Value!-", pattern: "[a-z0-9-]+"), Has.Count.EqualTo(1));
            Assert.That(Check("good-value-1", pattern: "[a-z0-9-]+"), Is.Empty);
            Assert.That(Check("ab", pattern: "a|ab"), Is.Empty);
        });
    }

    [Test]
    public void Check_EmptyPattern_IsNotChecked()
    {
        Assert.That(Check("anything", pattern: string.Empty), Is.Empty);
    }

    [Test]
    public void Check_FormPropertyInfo_UsesItsBounds()
    {
        var property = new FormPropertyInfo
        {
            Label = "Count",
            Min = 1,
            Max = 3,
            MaxLength = 2,
            Pattern = "[0-9]+",
            PatternErrorMessage = "digits"
        };

        Assert.Multiple(() =>
        {
            Assert.That(FormPropertyConstraints.Check(property, 7), Is.EqualTo(new[] { "Count must be at most 3." }));
            Assert.That(FormPropertyConstraints.Check(property, "abc"),
                Is.EqualTo(new[] { "Count must not exceed 2 characters.", "digits" }));
        });
    }

    [Test]
    public void MatchesPattern_Timeout_CountsAsAMismatch()
    {
        // Catastrophic backtracking (exponential in the run of letters): far longer than the timeout.
        var text = new string('a', 64) + "!";

        Assert.That(FormPropertyConstraints.MatchesPattern(text, @"(\w+\s?)*"), Is.False);
    }
}
