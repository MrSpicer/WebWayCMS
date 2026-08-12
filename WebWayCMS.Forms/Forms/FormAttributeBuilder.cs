using System.Text;
using System.Text.Encodings.Web;

namespace WebWayCMS.Forms;

/// <summary>
/// Fluent builder over a <see cref="FormFieldContext"/> that produces an HTML attribute string
/// encoded exactly once via <see cref="HtmlEncoder.Default"/>. Views emit the result with
/// <c>@Html.Raw(...)</c>.
/// </summary>
public sealed class FormAttributeBuilder
{
    private readonly FormFieldContext _field;
    private readonly StringBuilder _sb = new();

    private FormAttributeBuilder(FormFieldContext field)
    {
        _field = field ?? throw new ArgumentNullException(nameof(field));
    }

    /// <summary>
    /// Start building attributes for a <see cref="FormFieldContext"/>.
    /// </summary>
    public static FormAttributeBuilder For(FormFieldContext field) => new(field);

    /// <summary>
    /// Append an arbitrary attribute value. The name is not encoded; the value IS encoded.
    /// </summary>
    public FormAttributeBuilder Attr(string name, string value)
    {
        _sb.Append(' ');
        _sb.Append(name);
        _sb.Append("=\"");
        _sb.Append(HtmlEncoder.Default.Encode(value));
        _sb.Append('"');
        return this;
    }

    /// <summary>
    /// Append a literal attribute string (no value encoding). Use for keywords like <c>required</c>.
    /// </summary>
    public FormAttributeBuilder Raw(string raw)
    {
        _sb.Append(' ');
        _sb.Append(raw);
        return this;
    }

    /// <summary>
    /// Set the input type attribute.
    /// </summary>
    public FormAttributeBuilder Type(string type) => Attr("type", type);

    /// <summary>
    /// Set the CSS class attribute.
    /// </summary>
    public FormAttributeBuilder Css(string css)
    {
        var classAttr = css;
        if (!string.IsNullOrEmpty(_field.Property.CssClass))
            classAttr += " " + _field.Property.CssClass;
        return Attr("class", classAttr);
    }

    /// <summary>
    /// Emit name+id (model binding) or data-prop+id (JSON binding). Must be called once.
    /// </summary>
    public FormAttributeBuilder Naming()
    {
        if (!string.IsNullOrEmpty(_field.InputName))
        {
            Attr("name", _field.InputName);
            Attr("id", _field.ElementId);
        }
        if (_field.JsonBound)
        {
            Attr("data-prop", _field.Name);
        }
        if (string.IsNullOrEmpty(_field.InputName) && !_field.JsonBound)
        {
            Attr("id", _field.ElementId);
        }
        return this;
    }

    /// <summary>
    /// Emit the value attribute using <see cref="FormFieldContext.StringValue"/>.
    /// </summary>
    public FormAttributeBuilder Value() => Value(_field.StringValue);

    /// <summary>
    /// Emit the value attribute with an explicit string, encoded once.
    /// </summary>
    public FormAttributeBuilder Value(string explicitValue)
    {
        return Attr("value", explicitValue);
    }

    /// <summary>
    /// Emit placeholder when non-empty.
    /// </summary>
    public FormAttributeBuilder Placeholder()
    {
        if (!string.IsNullOrEmpty(_field.Placeholder))
            Attr("placeholder", _field.Placeholder);
        return this;
    }

    /// <summary>
    /// Emit maxlength when set.
    /// </summary>
    public FormAttributeBuilder MaxLength()
    {
        if (_field.MaxLength.HasValue)
            Raw($"maxlength=\"{_field.MaxLength.Value}\"");
        return this;
    }

    /// <summary>
    /// Emit pattern + title when set.
    /// </summary>
    public FormAttributeBuilder Pattern()
    {
        if (!string.IsNullOrEmpty(_field.Pattern))
        {
            Attr("pattern", _field.Pattern);
            if (!string.IsNullOrEmpty(_field.PatternErrorMessage))
                Attr("title", _field.PatternErrorMessage);
        }
        return this;
    }

    /// <summary>
    /// Emit min + max when set.
    /// </summary>
    public FormAttributeBuilder MinMax()
    {
        if (_field.Min.HasValue)
            Raw($"min=\"{_field.Min.Value}\"");
        if (_field.Max.HasValue)
            Raw($"max=\"{_field.Max.Value}\"");
        return this;
    }

    /// <summary>
    /// Emit required + aria-required when the field is required.
    /// </summary>
    public FormAttributeBuilder Required()
    {
        if (_field.IsRequired)
            Raw("required aria-required=\"true\"");
        return this;
    }

    /// <summary>
    /// Emit aria-describedby ONLY when HelpText is non-empty.
    /// </summary>
    public FormAttributeBuilder DescribedBy()
    {
        if (!string.IsNullOrEmpty(_field.HelpText))
        {
            var helpId = HtmlEncoder.Default.Encode(_field.ElementId) + "_help";
            Raw($"aria-describedby=\"{helpId}\"");
        }
        return this;
    }

    /// <summary>
    /// Emit a data-attribute with an encoded value.
    /// </summary>
    public FormAttributeBuilder Data(string key, string value)
    {
        if (!string.IsNullOrEmpty(value))
            Attr($"data-{key}", value);
        return this;
    }

    /// <summary>
    /// Build the full attribute string, HTML-encoded exactly once. Returned string is safe for
    /// <c>@Html.Raw(...)</c> emission.
    /// </summary>
    public string Build()
    {
        return _sb.ToString();
    }
}
