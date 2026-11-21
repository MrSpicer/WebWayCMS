using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;

using NUnit.Framework;

using WebWayCMS.Caching;
using WebWayCMS.Data.Services;
using WebWayCMS.Data.Services.Caching;
using WebWayCMS.Mcp;

namespace WebWayCMS.Host.Tests.Caching;

[TestFixture]
public class CmsPublicOutputCachePolicyTests
{
    private CmsCacheGeneration _generation = null!;
    private CmsPublicOutputCachePolicy _policy = null!;

    [SetUp]
    public void SetUp()
    {
        _generation = new CmsCacheGeneration();
        _policy = new CmsPublicOutputCachePolicy(
            _generation,
            Options.Create(new CacheOptions()),
            Options.Create(new McpOptions()));
    }

    private static OutputCacheContext NewContext(string path, string method = "GET", ClaimsPrincipal? user = null)
    {
        var httpContext = new DefaultHttpContext { Request = { Path = path, Method = method } };
        if (user is not null)
            httpContext.User = user;
        return new OutputCacheContext { HttpContext = httpContext };
    }

    private static ClaimsPrincipal Authenticated() =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "someone") }, "test"));

    [Test]
    public void Ctor_NullArguments_Throw()
    {
        Assert.That(() => new CmsPublicOutputCachePolicy(null!, Options.Create(new CacheOptions()), Options.Create(new McpOptions())),
            Throws.ArgumentNullException);
        Assert.That(() => new CmsPublicOutputCachePolicy(_generation, null!, Options.Create(new McpOptions())),
            Throws.ArgumentNullException);
        Assert.That(() => new CmsPublicOutputCachePolicy(_generation, Options.Create(new CacheOptions()), null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public async Task CacheRequestAsync_AnonymousGetOnPublicPath_EnablesCachingWithGenerationVaryAndQueryVary()
    {
        _generation.Advance(4);
        var context = NewContext("/");

        await _policy.CacheRequestAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.EnableOutputCaching, Is.True);
            Assert.That(context.AllowCacheLookup, Is.True);
            Assert.That(context.AllowCacheStorage, Is.True);
            Assert.That(context.CacheVaryByRules.VaryByValues["gen"], Is.EqualTo("4.0"));
            Assert.That(context.CacheVaryByRules.QueryKeys, Is.EqualTo("*"));
            Assert.That(context.Tags, Does.Contain(CmsPublicOutputCachePolicy.Tag));
            Assert.That(context.ResponseExpirationTimeSpan, Is.EqualTo(new CacheOptions().OutputCacheExpiration));
            Assert.That(context.HttpContext.Items["CmsCache:Generation"], Is.EqualTo("4.0"));
        });
    }

    [Test]
    public async Task CacheRequestAsync_HeadRequest_EnablesCaching()
    {
        var context = NewContext("/about", HttpMethods.Head);

        await _policy.CacheRequestAsync(context);

        Assert.That(context.EnableOutputCaching, Is.True);
    }

    [Test]
    public async Task CacheRequestAsync_PostRequest_DisablesCaching()
    {
        var context = NewContext("/", HttpMethods.Post);

        await _policy.CacheRequestAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.EnableOutputCaching, Is.False);
            Assert.That(context.CacheVaryByRules.VaryByValues, Is.Empty);
            Assert.That(context.Tags, Is.Empty);
        });
    }

    [Test]
    public async Task CacheRequestAsync_AuthenticatedUser_DisablesCaching()
    {
        var context = NewContext("/", user: Authenticated());

        await _policy.CacheRequestAsync(context);

        Assert.That(context.EnableOutputCaching, Is.False);
    }

    [Test]
    public async Task CacheRequestAsync_AuthorizationHeaderPresent_DisablesCaching()
    {
        var context = NewContext("/");
        context.HttpContext.Request.Headers["Authorization"] = "Bearer x";

        await _policy.CacheRequestAsync(context);

        Assert.That(context.EnableOutputCaching, Is.False);
    }

    [TestCase("/wadmin")]
    [TestCase("/wadmin/users")]
    [TestCase("/wadmin/")]
    [TestCase("/WADMIN/ROLES")]
    [TestCase("/_preview/123e4567-e89b-12d3-a456-426614174000")]
    [TestCase("/Identity/Account/Login")]
    [TestCase("/api/contentzones")]
    [TestCase("/mcp")]
    [TestCase("/media/deadbeef/image.png")]
    [TestCase("/.well-known/oauth-protected-resource")]
    public async Task CacheRequestAsync_ExcludedPath_DisablesCaching(string path)
    {
        var context = NewContext(path);

        await _policy.CacheRequestAsync(context);

        Assert.That(context.EnableOutputCaching, Is.False, $"path: {path}");
    }

    [TestCase("/wadmin-x")]
    [TestCase("/apix")]
    [TestCase("/mediacenter")]
    [TestCase("/home")]
    public async Task CacheRequestAsync_SimilarButNotSegmentBoundaries_EnablesCaching(string path)
    {
        var context = NewContext(path);

        await _policy.CacheRequestAsync(context);

        Assert.That(context.EnableOutputCaching, Is.True, $"path: {path}");
    }

    [Test]
    public async Task CacheRequestAsync_NullPathValue_TreatsAsEmptyPath()
    {
        var httpContext = new DefaultHttpContext { Request = { Path = new PathString((string?)null), Method = "GET" } };
        var context = new OutputCacheContext { HttpContext = httpContext };

        await _policy.CacheRequestAsync(context);

        Assert.That(context.EnableOutputCaching, Is.True);
    }

    [Test]
    public async Task CacheRequestAsync_CustomMcpPath_ExcludesThatPath()
    {
        var policy = new CmsPublicOutputCachePolicy(
            _generation,
            Options.Create(new CacheOptions()),
            Options.Create(new McpOptions { Path = "/agent" }));

        var excluded = NewContext("/agent");
        var similar = NewContext("/agent-x");
        await policy.CacheRequestAsync(excluded);
        await policy.CacheRequestAsync(similar);

        Assert.Multiple(() =>
        {
            Assert.That(excluded.EnableOutputCaching, Is.False);
            Assert.That(similar.EnableOutputCaching, Is.True);
        });
    }

    [Test]
    public async Task ServeFromCacheAsync_FollowsEnableOutputCaching()
    {
        var enabled = NewContext("/");
        enabled.EnableOutputCaching = true;
        var disabled = NewContext("/");
        disabled.EnableOutputCaching = false;

        await _policy.ServeFromCacheAsync(enabled);
        await _policy.ServeFromCacheAsync(disabled);

        Assert.Multiple(() =>
        {
            Assert.That(enabled.AllowCacheLookup, Is.True);
            Assert.That(disabled.AllowCacheLookup, Is.False);
        });
    }

    [Test]
    public async Task ServeResponseAsync_CacheableResponse_AllowsStorage()
    {
        var context = NewContext("/");
        context.EnableOutputCaching = true;
        await _policy.CacheRequestAsync(context);
        context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;

        await _policy.ServeResponseAsync(context);

        Assert.That(context.AllowCacheStorage, Is.True);
    }

    [Test]
    public async Task ServeResponseAsync_CachingNotEnabled_DisallowsStorage()
    {
        var context = NewContext("/");
        context.EnableOutputCaching = false;
        context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;

        await _policy.ServeResponseAsync(context);

        Assert.That(context.AllowCacheStorage, Is.False);
    }

    [Test]
    public async Task ServeResponseAsync_Non200_DisallowsStorage()
    {
        var context = NewContext("/");
        context.EnableOutputCaching = true;
        await _policy.CacheRequestAsync(context);
        context.HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;

        await _policy.ServeResponseAsync(context);

        Assert.That(context.AllowCacheStorage, Is.False);
    }

    [Test]
    public async Task ServeResponseAsync_SetCookieHeader_DisallowsStorage()
    {
        var context = NewContext("/");
        context.EnableOutputCaching = true;
        await _policy.CacheRequestAsync(context);
        context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
        context.HttpContext.Response.Headers["Set-Cookie"] = "theme=dark";

        await _policy.ServeResponseAsync(context);

        Assert.That(context.AllowCacheStorage, Is.False);
    }

    [TestCase("no-store")]
    [TestCase("private, max-age=60")]
    [TestCase("max-age=60, NO-STORE")]
    public async Task ServeResponseAsync_NoStoreOrPrivateCacheControl_DisallowsStorage(string cacheControl)
    {
        var context = NewContext("/");
        context.EnableOutputCaching = true;
        await _policy.CacheRequestAsync(context);
        context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
        context.HttpContext.Response.Headers["Cache-Control"] = cacheControl;

        await _policy.ServeResponseAsync(context);

        Assert.That(context.AllowCacheStorage, Is.False, $"Cache-Control: {cacheControl}");
    }

    [Test]
    public async Task ServeResponseAsync_NoGenerationRecorded_DisallowsStorage()
    {
        // EnableOutputCaching set without CacheRequestAsync running: no generation in the items.
        var context = NewContext("/");
        context.EnableOutputCaching = true;
        context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;

        await _policy.ServeResponseAsync(context);

        Assert.That(context.AllowCacheStorage, Is.False);
    }

    [Test]
    public async Task ServeResponseAsync_GenerationNotStoredAsString_DisallowsStorage()
    {
        var context = NewContext("/");
        context.EnableOutputCaching = true;
        await _policy.CacheRequestAsync(context);
        context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
        context.HttpContext.Items[CmsPublicOutputCachePolicy.GenerationItemKey] = 4L;

        await _policy.ServeResponseAsync(context);

        Assert.That(context.AllowCacheStorage, Is.False);
    }

    [Test]
    public async Task ServeResponseAsync_GenerationMovedMeanwhile_DisallowsStorage()
    {
        var context = NewContext("/");
        context.EnableOutputCaching = true;
        await _policy.CacheRequestAsync(context);
        context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;

        _generation.Advance(1);

        await _policy.ServeResponseAsync(context);

        Assert.That(context.AllowCacheStorage, Is.False);
    }

    [Test]
    public async Task ServeResponseAsync_LocalEpochMovedMeanwhile_DisallowsStorage()
    {
        var context = NewContext("/");
        context.EnableOutputCaching = true;
        await _policy.CacheRequestAsync(context);
        context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;

        _generation.AdvanceLocal();

        await _policy.ServeResponseAsync(context);

        Assert.That(context.AllowCacheStorage, Is.False);
    }

    [Test]
    public async Task ServeResponseAsync_DraftMarkedDuringRender_DisallowsStorage()
    {
        var context = NewContext("/");
        context.EnableOutputCaching = true;
        await _policy.CacheRequestAsync(context);
        context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
        context.HttpContext.Items[PreviewConstants.DraftRequestItemKey] = true;

        await _policy.ServeResponseAsync(context);

        Assert.That(context.AllowCacheStorage, Is.False);
    }

    [Test]
    public async Task ServeResponseAsync_DraftKeyPresentButNotTrue_AllowsStorage()
    {
        // A draft mark that is not exactly true (PreviewConstants stores true) is not a draft mark.
        var context = NewContext("/");
        context.EnableOutputCaching = true;
        await _policy.CacheRequestAsync(context);
        context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
        context.HttpContext.Items[PreviewConstants.DraftRequestItemKey] = false;

        await _policy.ServeResponseAsync(context);

        Assert.That(context.AllowCacheStorage, Is.True);
    }

    [Test]
    public async Task ServeResponseAsync_SnapshotMarkedDuringRender_DisallowsStorage()
    {
        var context = NewContext("/");
        context.EnableOutputCaching = true;
        await _policy.CacheRequestAsync(context);
        context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
        context.HttpContext.Items[PreviewConstants.SnapshotRequestItemKey] =
            new ContentReadSnapshot(new[] { Guid.NewGuid() }, new[] { Guid.NewGuid() });

        await _policy.ServeResponseAsync(context);

        Assert.That(context.AllowCacheStorage, Is.False);
    }
}
