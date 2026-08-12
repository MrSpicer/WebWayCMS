using NUnit.Framework;

namespace WebWayCMS.Forms.Tests;

[TestFixture]
public class FormValueFormatterTests
{
    // ── Format ──────────────────────────────────────────────────────────

    [Test]
    public void Format_Null_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.Format(null), Is.Empty);
    }

    [Test]
    public void Format_DateTime_ReturnsFormatted()
    {
        var dt = new DateTime(2024, 5, 6, 7, 8, 0);
        Assert.That(FormValueFormatter.Format(dt), Is.EqualTo("2024-05-06T07:08"));
    }

    [Test]
    public void Format_DateTimeMinValue_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.Format(DateTime.MinValue), Is.Empty);
    }

    [Test]
    public void Format_DateTimeOffset_ReturnsFormatted()
    {
        var dto = new DateTimeOffset(2024, 5, 6, 7, 8, 0, TimeSpan.Zero);
        Assert.That(FormValueFormatter.Format(dto), Is.EqualTo("2024-05-06T07:08"));
    }

    [Test]
    public void Format_DateTimeOffsetMinValue_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.Format(DateTimeOffset.MinValue), Is.Empty);
    }

    [Test]
    public void Format_DateOnly_ReturnsFormatted()
    {
        var d = new DateOnly(2024, 5, 6);
        Assert.That(FormValueFormatter.Format(d), Is.EqualTo("2024-05-06"));
    }

    [Test]
    public void Format_DateOnlyMinValue_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.Format(DateOnly.MinValue), Is.Empty);
    }

    [Test]
    public void Format_Guid_ReturnsString()
    {
        var g = Guid.NewGuid();
        Assert.That(FormValueFormatter.Format(g), Is.EqualTo(g.ToString()));
    }

    [Test]
    public void Format_GuidEmpty_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.Format(Guid.Empty), Is.Empty);
    }

    [Test]
    public void Format_BoolTrue_ReturnsTrue()
    {
        Assert.That(FormValueFormatter.Format(true), Is.EqualTo("True"));
    }

    [Test]
    public void Format_String_ReturnsString()
    {
        Assert.That(FormValueFormatter.Format("hello"), Is.EqualTo("hello"));
    }

    [Test]
    public void Format_NullStringType_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.Format(new NullStringType()), Is.Empty);
    }

    // ── FormatDateTime ──────────────────────────────────────────────────

    [Test]
    public void FormatDateTime_Normal_ReturnsFormatted()
    {
        var dt = new DateTime(2024, 5, 6, 7, 8, 0);
        Assert.That(FormValueFormatter.FormatDateTime(dt), Is.EqualTo("2024-05-06T07:08"));
    }

    [Test]
    public void FormatDateTime_Null_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.FormatDateTime(null), Is.Empty);
    }

    [Test]
    public void FormatDateTime_MinValue_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.FormatDateTime(DateTime.MinValue), Is.Empty);
    }

    // ── FormatDateTimeOffset ────────────────────────────────────────────

    [Test]
    public void FormatDateTimeOffset_Normal_ReturnsFormatted()
    {
        var dto = new DateTimeOffset(2024, 5, 6, 7, 8, 0, TimeSpan.Zero);
        Assert.That(FormValueFormatter.FormatDateTimeOffset(dto), Is.EqualTo("2024-05-06T07:08"));
    }

    [Test]
    public void FormatDateTimeOffset_Null_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.FormatDateTimeOffset(null), Is.Empty);
    }

    [Test]
    public void FormatDateTimeOffset_MinValue_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.FormatDateTimeOffset(DateTimeOffset.MinValue), Is.Empty);
    }

    // ── FormatDateOnly ──────────────────────────────────────────────────

    [Test]
    public void FormatDateOnly_Normal_ReturnsFormatted()
    {
        var d = new DateOnly(2024, 5, 6);
        Assert.That(FormValueFormatter.FormatDateOnly(d), Is.EqualTo("2024-05-06"));
    }

    [Test]
    public void FormatDateOnly_Null_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.FormatDateOnly(null), Is.Empty);
    }

    [Test]
    public void FormatDateOnly_MinValue_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.FormatDateOnly(DateOnly.MinValue), Is.Empty);
    }

    // ── FormatGuid ──────────────────────────────────────────────────────

    [Test]
    public void FormatGuid_Normal_ReturnsString()
    {
        var g = Guid.NewGuid();
        Assert.That(FormValueFormatter.FormatGuid(g), Is.EqualTo(g.ToString()));
    }

    [Test]
    public void FormatGuid_Null_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.FormatGuid(null), Is.Empty);
    }

    [Test]
    public void FormatGuid_Empty_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.FormatGuid(Guid.Empty), Is.Empty);
    }

    // ── FormatBool ──────────────────────────────────────────────────────

    [Test]
    public void FormatBool_True_ReturnsTrue()
    {
        Assert.That(FormValueFormatter.FormatBool(true), Is.EqualTo("true"));
    }

    [Test]
    public void FormatBool_False_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.FormatBool(false), Is.Empty);
    }

    [Test]
    public void FormatBool_Null_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.FormatBool(null), Is.Empty);
    }

    // ── FormatDateValue ─────────────────────────────────────────────────

    [Test]
    public void FormatDateValue_Null_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.FormatDateValue(null), Is.Empty);
    }

    [Test]
    public void FormatDateValue_DateTime_ReturnsFormatted()
    {
        var dt = new DateTime(2024, 5, 6, 10, 30, 0);
        Assert.That(FormValueFormatter.FormatDateValue(dt), Is.EqualTo("2024-05-06"));
    }

    [Test]
    public void FormatDateValue_DateTimeMinValue_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.FormatDateValue(DateTime.MinValue), Is.Empty);
    }

    [Test]
    public void FormatDateValue_DateTimeOffset_ReturnsFormatted()
    {
        var dto = new DateTimeOffset(2024, 5, 6, 0, 0, 0, TimeSpan.Zero);
        Assert.That(FormValueFormatter.FormatDateValue(dto), Is.EqualTo("2024-05-06"));
    }

    [Test]
    public void FormatDateValue_DateTimeOffsetMinValue_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.FormatDateValue(DateTimeOffset.MinValue), Is.Empty);
    }

    [Test]
    public void FormatDateValue_DateOnly_ReturnsFormatted()
    {
        var d = new DateOnly(2024, 5, 6);
        Assert.That(FormValueFormatter.FormatDateValue(d), Is.EqualTo("2024-05-06"));
    }

    [Test]
    public void FormatDateValue_DateOnlyMinValue_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.FormatDateValue(DateOnly.MinValue), Is.Empty);
    }

    [Test]
    public void FormatDateValue_UnsupportedType_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.FormatDateValue(42), Is.Empty);
    }

    [Test]
    public void FormatDateValue_String_ReturnsEmpty()
    {
        Assert.That(FormValueFormatter.FormatDateValue("2024-05-06"), Is.Empty);
    }
}
