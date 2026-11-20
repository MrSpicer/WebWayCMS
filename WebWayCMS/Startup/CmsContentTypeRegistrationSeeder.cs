using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Data.Services.ContentTypes;
using WebWayCMS.Data.Services.Registrations;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsContentTypeRegistrationSeeder
{
    /// <summary>
    /// One registration per content type in <see cref="IContentTypeRegistry"/> (the DI descriptors, so host types
    /// registered through the builder are included; no assembly scan). The CLR type name is code-owned; the names
    /// start from the descriptor, else the type's admin handler (admin mode), else the CLR type. Runs in both
    /// modes, since they share the database. Afterwards, content nodes whose key nothing registers are reported.
    /// </summary>
    internal static WebApplication EnsureContentTypeRegistrationsSeeded(this WebApplication app, bool throwOnError = false)
        => app.EnsureRegistrationsSynced(
            "content type",
            "WEBWAYCMS_SKIP_DEFAULTCONTENTTYPES",
            throwOnError,
            services => new VersionedRegistrationSyncTarget<ContentTypeRegistrationDTO>(
                services.GetRequiredService<CmsDbContext>(),
                services.GetRequiredService<IContentStore<ContentTypeRegistrationDTO>>(),
                r => r.ContentTypeKey),
            (services, _) => Declared(
                services.GetRequiredService<IContentTypeRegistry>(),
                services.GetService<IAdminHandlerRegistry>()),
            WarnUnregisteredNodeKeys);

    private static IEnumerable<RegistrationSyncItem<ContentTypeRegistrationDTO>> Declared(
        IContentTypeRegistry contentTypes, IAdminHandlerRegistry? handlers)
    {
        foreach (var descriptor in contentTypes.All)
        {
            var clrTypeName = descriptor.ClrType.FullName ?? descriptor.ClrType.Name;
            yield return new RegistrationSyncItem<ContentTypeRegistrationDTO>(
                descriptor.Key,
                () => Create(descriptor, clrTypeName, handlers?.GetHandler(descriptor.Key)?.DisplayName),
                row =>
                {
                    if (row.ClrTypeName == clrTypeName)
                        return false;
                    row.ClrTypeName = clrTypeName;
                    return true;
                });
        }
    }

    private static ContentTypeRegistrationDTO Create(ContentTypeDescriptor descriptor, string clrTypeName, string? handlerDisplayName)
    {
        var displayName = descriptor.DisplayName
            ?? (string.IsNullOrWhiteSpace(handlerDisplayName) ? null : handlerDisplayName)
            ?? ContentTypeDescriptor.DeriveDisplayName(descriptor.ClrType);
        var pluralDisplayName = descriptor.PluralDisplayName ?? ContentTypeDescriptor.Pluralize(displayName);

        return new ContentTypeRegistrationDTO
        {
            Version = new ContentVersion
            {
                Title = displayName,
                Slug = descriptor.Key,
            },
            ContentTypeKey = descriptor.Key,
            ClrTypeName = clrTypeName,
            DisplayName = displayName,
            PluralDisplayName = pluralDisplayName,
        };
    }

    private static void WarnUnregisteredNodeKeys(IServiceProvider services, RegistrationSyncResult result, ILogger logger)
    {
        var contentTypes = services.GetRequiredService<IContentTypeRegistry>();
        var nodeKeys = services.GetRequiredService<CmsDbContext>().Set<ContentNode>()
            .Select(n => n.ContentTypeKey)
            .Distinct()
            .ToList();

        foreach (var key in nodeKeys.Where(k => contentTypes.Get(k) == null
                                                && !result.Orphaned.Contains(k, StringComparer.OrdinalIgnoreCase)))
        {
            logger.Warning("Content nodes use content type key '{Key}', which has no content type registration", key);
        }
    }
}
