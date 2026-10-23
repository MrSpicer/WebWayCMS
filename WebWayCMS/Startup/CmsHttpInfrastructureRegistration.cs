using System.Diagnostics.CodeAnalysis;
using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using IPNetwork = System.Net.IPNetwork;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsHttpInfrastructureRegistration
{
    internal static void ConfigureForwardedHeaders(IServiceCollection services, IConfiguration? configuration = null)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // By default (no configuration), the framework's loopback-only trust list is kept: a
            // reverse proxy running on the same machine is trusted, and requests from any other
            // source have their X-Forwarded-* headers ignored (spoofing is safe). A host behind a
            // remote proxy lists the proxy's IPs under "ForwardedHeaders:KnownProxies"; a host
            // behind a proxy on a shared subnet can use "ForwardedHeaders:KnownIPNetworks". A host
            // whose proxy address is not stable (e.g. a container network, like the shipped
            // docker-compose) can set "ForwardedHeaders:TrustAllProxies" to accept the headers from
            // any source — only safe when nothing but the proxy can reach the app.
            if (configuration is null)
                return;

            if (configuration.GetValue<bool>("ForwardedHeaders:TrustAllProxies"))
            {
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
                return;
            }

            var knownProxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>();
            if (knownProxies is { Length: > 0 })
            {
                options.KnownProxies.Clear();
                foreach (var proxy in knownProxies)
                {
                    if (IPAddress.TryParse(proxy, out var ip))
                        options.KnownProxies.Add(ip);
                }
            }

            var knownNetworks = configuration.GetSection("ForwardedHeaders:KnownIPNetworks").Get<string[]>();
            if (knownNetworks is { Length: > 0 })
            {
                options.KnownIPNetworks.Clear();
                foreach (var network in knownNetworks)
                {
                    if (IPNetwork.TryParse(network, out var subnet))
                        options.KnownIPNetworks.Add(subnet);
                }
            }
        });
    }

    internal static void ConfigureRateLimiting(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(
                AuthRateLimiting.GetPartition);
        });
    }
}
