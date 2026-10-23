using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Services;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class FormFieldViewResolverTests
{
    private const string Component = "FormText";

    private IViewDiscoveryService _discovery = null!;
    private FormFieldViewResolver _resolver = null!;

    [SetUp]
    public void SetUp()
    {
        _discovery = Substitute.For<IViewDiscoveryService>();
        _resolver = new FormFieldViewResolver(_discovery);
    }

    // NSubstitute: the out parameter is set inside the Returns callback via the args array
    // (index 3 = the 'out string? viewName' of TryResolveView). Unconfigured calls return false.
    private void Hit(FormFieldRenderMode mode, string? variant, string view)
        => _discovery.TryResolveView(Component, mode, variant, out Arg.Any<string?>())
            .Returns(x => { x[3] = view; return true; });

    // Registers every view file in the list ("{prefix}.{variant}") as discoverable.
    private void Views(params string[] views)
    {
        foreach (var view in views)
        {
            var dot = view.IndexOf('.');
            var mode = Enum.GetValues<FormFieldRenderMode>().Single(m => m.ToFilePrefix() == view[..dot]);
            Hit(mode, view[(dot + 1)..], view);
        }
    }

    [Test]
    public void Constructor_NullDiscovery_Throws()
        => Assert.That(() => new FormFieldViewResolver(null!), Throws.ArgumentNullException);

    // Each case: the views that exist, and the view the chain must pick. Every chain step is
    // exercised by removing the views ahead of it.
    private static IEnumerable<TestCaseData> ChainCases()
    {
        var all = new[]
        {
            "write.v", "write.default", "read.v", "read.default",
            "adminwrite.v", "adminwrite.default", "adminread.v", "adminread.default"
        };

        string[][] chains =
        [
            ["write.v", "write.default", "adminwrite.v", "adminwrite.default"],
            ["read.v", "read.default", "write.v", "write.default", "adminread.v", "adminread.default", "adminwrite.v", "adminwrite.default"],
            ["adminwrite.v", "adminwrite.default", "write.v", "write.default"],
            ["adminread.v", "adminread.default", "adminwrite.v", "adminwrite.default", "read.v", "read.default", "write.v", "write.default"]
        ];
        FormFieldRenderMode[] modes =
            [FormFieldRenderMode.Write, FormFieldRenderMode.Read, FormFieldRenderMode.AdminWrite, FormFieldRenderMode.AdminRead];

        for (var m = 0; m < modes.Length; m++)
        {
            var chain = chains[m];
            for (var i = 0; i < chain.Length; i++)
            {
                // Everything except the steps ahead of i (views off the chain stay present, so
                // they must never be picked).
                var present = all.Except(chain.Take(i)).ToArray();
                yield return new TestCaseData(modes[m], present, chain[i])
                    .SetName($"Resolve_{modes[m]}_Step{i + 1}_Picks_{chain[i]}");
            }
        }
    }

    [TestCaseSource(nameof(ChainCases))]
    public void Resolve_FollowsTheModeChain(FormFieldRenderMode mode, string[] present, string expected)
    {
        Views(present);

        Assert.That(_resolver.Resolve(Component, mode, "v"), Is.EqualTo(expected));
    }

    [Test]
    public void Resolve_WriteChain_NeverUsesReadViews()
    {
        Views("read.v", "read.default", "adminread.v", "adminread.default");

        Assert.Multiple(() =>
        {
            Assert.That(_resolver.Resolve(Component, FormFieldRenderMode.Write, "v"), Is.Null);
            Assert.That(_resolver.Resolve(Component, FormFieldRenderMode.AdminWrite, "v"), Is.Null);
        });
    }

    [Test]
    public void Resolve_EmptyOrNullVariant_PassesItThroughToDiscovery()
    {
        Hit(FormFieldRenderMode.AdminWrite, null, "adminwrite.default");
        Hit(FormFieldRenderMode.AdminWrite, "", "adminwrite.default");

        Assert.Multiple(() =>
        {
            Assert.That(_resolver.Resolve(Component, FormFieldRenderMode.AdminWrite, null), Is.EqualTo("adminwrite.default"));
            Assert.That(_resolver.Resolve(Component, FormFieldRenderMode.AdminWrite, ""), Is.EqualTo("adminwrite.default"));
        });
    }

    [Test]
    public void Resolve_NoViews_ReturnsNull()
        => Assert.That(_resolver.Resolve(Component, FormFieldRenderMode.AdminRead, "v"), Is.Null);
}
