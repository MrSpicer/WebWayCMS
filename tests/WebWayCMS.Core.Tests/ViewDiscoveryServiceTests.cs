using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Razor.Hosting;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
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

        // Host: mode-prefixed component views (+ a partial to be skipped) and an area.
        WriteView(Path.Combine(_root, "Views", "Shared", "Components", "Widget"), "view.default.cshtml");
        WriteView(Path.Combine(_root, "Views", "Shared", "Components", "Widget"), "view.compact.cshtml");
        WriteView(Path.Combine(_root, "Views", "Shared", "Components", "Widget"), "edit.default.cshtml");
        WriteView(Path.Combine(_root, "Views", "Shared", "Components", "Widget"), "list.default.cshtml");
        WriteView(Path.Combine(_root, "Views", "Shared", "Components", "Widget"), "admin.nav.cshtml");
        WriteView(Path.Combine(_root, "Views", "Shared", "Components", "Widget"), "adminlist.table.cshtml");
        WriteView(Path.Combine(_root, "Views", "Shared", "Components", "Widget"), "_partial.cshtml");
        WriteView(Path.Combine(_root, "Views", "Home"), "Index.cshtml");
        WriteView(Path.Combine(_root, "Areas", "Admin", "Views", "Shared", "Components", "Widget"), "view.area.cshtml");

        // Sibling library: component views the view engine never searches for a non-area render, so
        // discovery must not report them (controller views are still scanned here).
        WriteView(Path.Combine(lib, "Views", "Shared", "Components", "Widget"), "view.lib.cshtml");
        WriteView(Path.Combine(lib, "Views", "Shared", "Components", "Widget"), "edit.lib.cshtml");
        WriteView(Path.Combine(lib, "Views", "Home"), "LibHome.cshtml");
        WriteView(Path.Combine(lib, "Areas", "Pub", "Views", "Shared", "Components", "Widget"), "edit.libarea.cshtml");

        var env = Substitute.For<IWebHostEnvironment>();
        env.ContentRootPath.Returns(_root);

        // Compiled views (the only source available in Release/Docker where no files exist on disk).
        var apm = BuildPartManager(
            "/Views/Shared/Components/Widget/view.compiled.cshtml",
            "/Areas/Admin/Views/Shared/Components/Widget/view.compiledarea.cshtml",
            "/Views/Shared/Components/Widget/_compiledpartial.cshtml",
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
    public void GetAvailableViews_CachesEachComponentScanForTheServiceLifetime()
    {
        var before = _service.GetAvailableViews("Widget", WidgetRenderMode.View);

        // A file added after the first scan is not seen by the same instance (the host shares one
        // instance outside Development and creates one per request in Development).
        WriteView(Path.Combine(_root, "Views", "Shared", "Components", "Widget"), "view.added.cshtml");
        var after = _service.GetAvailableViews("Widget", WidgetRenderMode.View);
        var resolved = _service.TryResolveView("Widget", WidgetRenderMode.View, "added", out _);

        Assert.Multiple(() =>
        {
            Assert.That(after, Is.EqualTo(before));
            Assert.That(resolved, Is.False);
        });
    }

    [Test]
    public void GetAvailableViews_Whitespace_ReturnsEmpty()
    {
        Assert.That(_service.GetAvailableViews("  ", WidgetRenderMode.View), Is.Empty);
    }

    [Test]
    public void GetAvailableViews_ListsOnlyThatModeStrippingPrefix_SkippingPartialsAndOtherModes()
    {
        var viewModes = _service.GetAvailableViews("Widget", WidgetRenderMode.View);
        var editModes = _service.GetAvailableViews("Widget", WidgetRenderMode.Edit);
        var listModes = _service.GetAvailableViews("Widget", WidgetRenderMode.List);

        Assert.Multiple(() =>
        {
            // View mode: host content root + compiled non-area parts, prefix stripped.
            Assert.That(viewModes, Does.Contain("default"));
            Assert.That(viewModes, Does.Contain("compact"));
            Assert.That(viewModes, Does.Contain("compiled"));
            Assert.That(viewModes, Does.Not.Contain("partial"));
            Assert.That(viewModes, Does.Not.Contain("compiledpartial"));

            // Areas and sibling folders are not non-area view-component locations: a hit there
            // would stop the fallback chain and then fail to load.
            Assert.That(viewModes, Does.Not.Contain("area"));
            Assert.That(viewModes, Does.Not.Contain("lib"));
            Assert.That(viewModes, Does.Not.Contain("compiledarea"));

            // Edit mode only sees edit.* files from the same locations.
            Assert.That(editModes, Does.Contain("default"));
            Assert.That(editModes, Does.Not.Contain("lib"));
            Assert.That(editModes, Does.Not.Contain("libarea"));
            Assert.That(editModes, Does.Not.Contain("compact"));

            // List mode only sees list.* files.
            Assert.That(listModes, Does.Contain("default"));
            Assert.That(listModes, Does.Not.Contain("compact"));
        });
    }

    [Test]
    public void GetAvailableViews_AdminAndAdminListPrefixesDoNotOverlap()
    {
        var admin = _service.GetAvailableViews("Widget", WidgetRenderMode.Admin);
        var adminList = _service.GetAvailableViews("Widget", WidgetRenderMode.AdminList);

        Assert.Multiple(() =>
        {
            Assert.That(admin, Is.EqualTo(new[] { "nav" }));
            Assert.That(adminList, Is.EqualTo(new[] { "table" }));
            Assert.That(_service.TryResolveView("Widget", WidgetRenderMode.Admin, "table", out _), Is.False);
            Assert.That(_service.TryResolveView("Widget", WidgetRenderMode.AdminList, "table", out var view), Is.True);
            Assert.That(view, Is.EqualTo("adminlist.table"));
        });
    }

    [Test]
    public void TryResolveView_ResolvesCaseInsensitiveReturningRealCasing()
    {
        Assert.Multiple(() =>
        {
            // Explicit variant resolves to the full file name.
            Assert.That(_service.TryResolveView("Widget", WidgetRenderMode.View, "compact", out var compact), Is.True);
            Assert.That(compact, Is.EqualTo("view.compact"));

            // Case-insensitive: "Compact" -> real casing "view.compact".
            Assert.That(_service.TryResolveView("Widget", WidgetRenderMode.View, "Compact", out var ci), Is.True);
            Assert.That(ci, Is.EqualTo("view.compact"));

            // Edit mode resolves its own file.
            Assert.That(_service.TryResolveView("Widget", WidgetRenderMode.Edit, "default", out var edit), Is.True);
            Assert.That(edit, Is.EqualTo("edit.default"));
        });
    }

    [Test]
    public void TryResolveView_AreaOrSiblingOnlyView_ReturnsFalseSoTheCallerFallsBack()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_service.TryResolveView("Widget", WidgetRenderMode.View, "area", out _), Is.False);
            Assert.That(_service.TryResolveView("Widget", WidgetRenderMode.View, "compiledarea", out _), Is.False);
            Assert.That(_service.TryResolveView("Widget", WidgetRenderMode.View, "lib", out _), Is.False);
        });
    }

    [Test]
    public void TryResolveView_EmptyOrDefaultVariant_ResolvesDefault()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_service.TryResolveView("Widget", WidgetRenderMode.View, null, out var fromNull), Is.True);
            Assert.That(fromNull, Is.EqualTo("view.default"));

            Assert.That(_service.TryResolveView("Widget", WidgetRenderMode.View, "  ", out var fromBlank), Is.True);
            Assert.That(fromBlank, Is.EqualTo("view.default"));

            Assert.That(_service.TryResolveView("Widget", WidgetRenderMode.View, "DEFAULT", out var fromDefault), Is.True);
            Assert.That(fromDefault, Is.EqualTo("view.default"));
        });
    }

    [Test]
    public void TryResolveView_Missing_MissingMode_MissingComponent_ReturnsFalse()
    {
        Assert.Multiple(() =>
        {
            // No view.default? It exists, so use a variant that does not.
            Assert.That(_service.TryResolveView("Widget", WidgetRenderMode.View, "nope", out var missing), Is.False);
            Assert.That(missing, Is.Null);

            // Admin mode has no files at all for this component.
            Assert.That(_service.TryResolveView("Widget", WidgetRenderMode.Admin, "default", out var admin), Is.False);
            Assert.That(admin, Is.Null);

            // Whitespace component.
            Assert.That(_service.TryResolveView("  ", WidgetRenderMode.View, "default", out var blank), Is.False);
            Assert.That(blank, Is.Null);

            // Unknown component.
            Assert.That(_service.TryResolveView("DoesNotExist", WidgetRenderMode.View, "default", out var unknown), Is.False);
            Assert.That(unknown, Is.Null);
        });
    }

    [Test]
    public void TryResolveView_FormFieldMode_ResolvesItsOwnPrefixOnly()
    {
        var dir = Path.Combine(_root, "Views", "Shared", "Components", "FormText");
        WriteView(dir, "adminwrite.default.cshtml");
        WriteView(dir, "AdminRead.Compact.cshtml");
        WriteView(dir, "Write.cshtml"); // un-prefixed: ignored

        Assert.Multiple(() =>
        {
            Assert.That(_service.TryResolveView("FormText", FormFieldRenderMode.AdminWrite, null, out var write), Is.True);
            Assert.That(write, Is.EqualTo("adminwrite.default"));
            Assert.That(_service.TryResolveView("FormText", FormFieldRenderMode.AdminRead, "compact", out var read), Is.True);
            Assert.That(read, Is.EqualTo("AdminRead.Compact"));
            Assert.That(_service.TryResolveView("FormText", FormFieldRenderMode.Write, "default", out var publicWrite), Is.False);
            Assert.That(publicWrite, Is.Null);
            Assert.That(_service.TryResolveView(" ", FormFieldRenderMode.AdminWrite, "default", out _), Is.False);
        });
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

            Assert.That(() => _service.GetAvailableViews("Locked", WidgetRenderMode.View), Throws.Nothing);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Test]
    public void GetControllerViews_Whitespace_ReturnsEmpty()
    {
        Assert.That(_service.GetControllerViews(" "), Is.Empty);
    }

    [Test]
    public void GetControllerViews_UnreadableDirectory_IsCaughtAndDoesNotThrow()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file-mode permissions required to force a directory read error.");
            return;
        }

        var locked = Path.Combine(_root, "Views", "LockedCtrl");
        Directory.CreateDirectory(locked);
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            var denied = false;
            try { Directory.GetFiles(locked); }
            catch { denied = true; }
            if (!denied)
                Assert.Ignore("Process can read the directory (likely running as root).");

            Assert.That(() => _service.GetControllerViews("LockedCtrl"), Throws.Nothing);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
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
