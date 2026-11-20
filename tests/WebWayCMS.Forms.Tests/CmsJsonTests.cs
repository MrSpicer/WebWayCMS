using System.Text.Json;

using NUnit.Framework;

namespace WebWayCMS.Forms.Tests;

[TestFixture]
public class CmsJsonTests
{
    private sealed class Sample
    {
        public string? Name { get; set; }
    }

    [Test]
    public void CaseInsensitive_ReadsAnyCasing()
        => Assert.That(JsonSerializer.Deserialize<Sample>("{\"NAME\":\"x\"}", CmsJson.CaseInsensitive)!.Name, Is.EqualTo("x"));
}
