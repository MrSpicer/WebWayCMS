using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Razor.Hosting;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Services;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class ViewDiscoveryServiceTests
{
    private string _root = null!;     // content root ("Host")
    private string _parent = null!;   // parent containing Host + sibling Lib
    private ViewDiscoveryService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _parent = Path.Combine(Path.GetTempPath(), "vds-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_parent, "Host");
        var lib = Path.Combine(_parent, "Lib");

        // Host: main component views (+ a partial to be skipped) and an area.
        WriteView(Path.Combine(_root, "Views", "Shared", "Components", "Widget"), "Default.cshtml");
        WriteView(Path.Combine(_root, "Views", "Shared", "Components", "Widget"), "Compact.cshtml");
        WriteView(Path.Combine(_root, "Views", "Shared", "Components", "Widget"), "_Partial.cshtml");
        WriteView(Path.Combine(_root, "Views", "Home"), "Index.cshtml");
        WriteView(Path.Combine(_root, "Areas", "Admin", "Views", "Shared", "Components", "Widget"), "AreaView.cshtml");

        // Sibling library: component + area views discovered via the parent-dir scan.
        WriteView(Path.Combine(lib, "Views", "Shared", "Components", "Widget"), "LibView.cshtml");
        WriteView(Path.Combine(lib, "Views", "Home"), "LibHome.cshtml");
        WriteView(Path.Combine(lib, "Areas", "Pub", "Views", "Shared", "Components", "Widget"), "LibArea.cshtml");

        var env = Substitute.For<IWebHostEnvironment>();
        env.ContentRootPath.Returns(_root);

        // Compiled views (the only source available in Release/Docker where no files exist on disk).
        var apm = BuildPartManager(
            "/Views/Shared/Components/Widget/Compiled.cshtml",
            "/Areas/Admin/Views/Shared/Components/Widget/CompiledArea.cshtml",
            "/Views/Shared/Components/Widget/_CompiledPartial.cshtml",
            "/Views/Home/CompiledHome.cshtml",
            "/Solo.cshtml"); // too short to match either pattern

        _service = new ViewDiscoveryService(env, apm);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_parent))
            Directory.Delete(_parent, recursive: true);
    }

    private static void WriteView(string dir, string file)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, file), "<p></p>");
    }

    /// <summary>
    /// Builds a real <see cref="ApplicationPartManager"/> wired the same way the running app wires it
    /// (AddControllersWithViews registers the MVC views feature provider), then adds a fake part
    /// exposing compiled Razor items at the given relative paths.
    /// </summary>
    private static ApplicationPartManager BuildPartManager(params string[] identifiers)
    {
        var apm = new ServiceCollection().AddControllersWithViews().PartManager;
        apm.ApplicationParts.Add(new FakeCompiledViewPart(identifiers));
        return apm;
    }

    [Test]
    public void Constructor_NullEnv_Throws()
    {
        Assert.That(() => new ViewDiscoveryService(null!, new ApplicationPartManager()), Throws.ArgumentNullException);
    }

    [Test]
    public void Constructor_NullApm_Throws()
    {
        var env = Substitute.For<IWebHostEnvironment>();
        Assert.That(() => new ViewDiscoveryService(env, null!), Throws.ArgumentNullException);
    }

    [Test]
    public void GetAvailableViews_Whitespace_ReturnsEmpty()
    {
        Assert.That(_service.GetAvailableViews("  "), Is.Empty);
    }

    [Test]
    public void ScanDirectory_UnreadableDirectory_IsCaughtAndDoesNotThrow()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file-mode permissions required to force a directory read error.");
            return;
        }

        var locked = Path.Combine(_root, "Views", "Shared", "Components", "Locked");
        Directory.CreateDirectory(locked);
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            var denied = false;
            try { Directory.GetFiles(locked); }
            catch { denied = true; }
            if (!denied)
                Assert.Ignore("Process can read the directory (likely running as root).");

            Assert.That(() => _service.GetAvailableViews("Locked"), Throws.Nothing);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Test]
    public void GetAvailableViews_AggregatesFilesystemAndCompiledViews_SkippingPartials()
    {
        var views = _service.GetAvailableViews("Widget");

        Assert.Multiple(() =>
        {
            // Filesystem (debug) sources.
            Assert.That(views, Does.Contain("Default"));
            Assert.That(views, Does.Contain("Compact"));
            Assert.That(views, Does.Contain("AreaView"));
            Assert.That(views, Does.Contain("LibView"));
            Assert.That(views, Does.Contain("LibArea"));
            Assert.That(views, Does.Not.Contain("_Partial"));

            // Compiled (Release/Docker) sources, including an area-scoped one.
            Assert.That(views, Does.Contain("Compiled"));
            Assert.That(views, Does.Contain("CompiledArea"));
            Assert.That(views, Does.Not.Contain("_CompiledPartial"));
        });
    }

    [Test]
    public void GetControllerViews_Whitespace_ReturnsEmpty()
    {
        Assert.That(_service.GetControllerViews(" "), Is.Empty);
    }

    [Test]
    public void GetControllerViews_AggregatesFilesystemAndCompiledViews()
    {
        var views = _service.GetControllerViews("Home");

        Assert.Multiple(() =>
        {
            Assert.That(views, Does.Contain("Index"));
            Assert.That(views, Does.Contain("LibHome"));
            Assert.That(views, Does.Contain("CompiledHome"));
        });
    }

    [Test]
    public void GetAvailableViews_UnknownComponent_ReturnsEmpty()
    {
        Assert.That(_service.GetAvailableViews("DoesNotExist"), Is.Empty);
    }

    [Test]
    public void GetControllerViews_UnknownController_ReturnsEmpty()
    {
        Assert.That(_service.GetControllerViews("DoesNotExist"), Is.Empty);
    }

    /// <summary>An application part that exposes a fixed set of compiled Razor view items.</summary>
    private sealed class FakeCompiledViewPart : ApplicationPart, IRazorCompiledItemProvider
    {
        private readonly string[] _identifiers;

        public FakeCompiledViewPart(string[] identifiers) => _identifiers = identifiers;

        public override string Name => "FakeCompiledViews";

        public IEnumerable<RazorCompiledItem> CompiledItems =>
            _identifiers.Select(id => (RazorCompiledItem)new FakeCompiledItem(id));
    }

    /// <summary>A compiled Razor view item identified by its relative path.</summary>
    private sealed class FakeCompiledItem : RazorCompiledItem
    {
        public FakeCompiledItem(string identifier) => Identifier = identifier;

        public override string Identifier { get; }
        public override string Kind => "mvc.1.0.view";
        public override IReadOnlyList<object> Metadata => Array.Empty<object>();
        public override Type Type => typeof(object);
    }
}