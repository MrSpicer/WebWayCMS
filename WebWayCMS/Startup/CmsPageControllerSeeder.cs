using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers;
using WebWayCMS.Controllers.Admin;
using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Data.Services.Registrations;
using WebWayCMS.Forms;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsPageControllerSeeder
{
    /// <summary>
    /// One registration per <c>[PageController]</c> controller. The configuration type and its property
    /// definitions are code-owned, kept in line on every boot.
    /// </summary>
    internal static WebApplication EnsurePageControllerRegistrationsSeeded(this WebApplication app, bool throwOnError = false)
        => app.EnsureRegistrationsSynced(
            "page controller",
            "WEBWAYCMS_SKIP_DEFAULTPAGECONTROLLERS",
            throwOnError,
            services => new VersionedRegistrationSyncTarget<PageControllerRegistrationDTO>(
                services.GetRequiredService<CmsDbContext>(),
                services.GetRequiredService<IContentStore<PageControllerRegistrationDTO>>(),
                p => p.ControllerName),
            (services, logger) => Declared(
                CmsStartupHelpers.SeedAssemblies(
                    services,
                    typeof(GenericPageController).Assembly,
                    typeof(AdminContentController).Assembly),
                logger));

    private static IEnumerable<RegistrationSyncItem<PageControllerRegistrationDTO>> Declared(IEnumerable<Assembly> assemblies, ILogger logger)
    {
        foreach (var (type, attribute) in CmsRegistrationSync.TypesWith<PageControllerAttribute>(
                     assemblies,
                     t => typeof(Microsoft.AspNetCore.Mvc.Controller).IsAssignableFrom(t)
                          && !typeof(Microsoft.AspNetCore.Mvc.ViewComponent).IsAssignableFrom(t),
                     logger))
        {
            var controllerName = CmsStartupHelpers.GetControllerName(type);

            var attributeConfigType = attribute.ConfigurationType;
            var baseConfigType = ResolveConfigTypeFromBaseClass(type);
            var effectiveConfigType = attributeConfigType ?? baseConfigType;
            if (attributeConfigType != null && baseConfigType != null && attributeConfigType != baseConfigType)
                logger.Warning("Page controller '{ControllerName}' declares ConfigurationType={AttributeType} but its PageControllerBase<T> generic parameter is {BaseType} — they differ.", controllerName, attributeConfigType.FullName, baseConfigType.FullName);

            var propertyDefinitionsJson = PropertyDefinitionsJson.Empty;
            if (effectiveConfigType != null)
            {
                try
                {
                    propertyDefinitionsJson = PropertyDefinitionsJson.Build(effectiveConfigType);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Failed to build property definitions for page controller '{ControllerName}'", controllerName);
                }
            }

            var configTypeName = effectiveConfigType?.FullName;
            yield return new RegistrationSyncItem<PageControllerRegistrationDTO>(
                controllerName,
                () => Create(type, controllerName, attribute, configTypeName, propertyDefinitionsJson),
                row =>
                {
                    if (row.ConfigurationTypeName == configTypeName && row.PropertyDefinitionsJson == propertyDefinitionsJson)
                        return false;
                    row.ConfigurationTypeName = configTypeName;
                    row.PropertyDefinitionsJson = propertyDefinitionsJson;
                    return true;
                });
        }
    }

    private static PageControllerRegistrationDTO Create(
        Type type, string controllerName, PageControllerAttribute attribute, string? configTypeName, string propertyDefinitionsJson)
    {
        var displayName = string.IsNullOrEmpty(attribute.DisplayName)
            ? FormPropertyBuilder.InsertSpaces(controllerName)
            : attribute.DisplayName;

        return new PageControllerRegistrationDTO
        {
            Version = new ContentVersion
            {
                Title = displayName,
                Slug = controllerName.ToLowerInvariant(),
            },
            ControllerName = controllerName,
            ControllerTypeName = type.FullName ?? type.Name,
            DisplayName = displayName,
            Description = attribute.Description ?? string.Empty,
            Category = attribute.Category ?? "General",
            IconClass = attribute.IconClass ?? string.Empty,
            Order = attribute.Order,
            ConfigurationTypeName = configTypeName,
            PropertyDefinitionsJson = propertyDefinitionsJson,
            IsActive = true,
        };
    }

    internal static Type? ResolveConfigTypeFromBaseClass(Type controllerType)
    {
        var baseType = controllerType.BaseType;
        while (baseType != null)
        {
            if (baseType.IsGenericType
                && baseType.GetGenericTypeDefinition() == typeof(PageControllerBase<>))
            {
                return baseType.GetGenericArguments()[0];
            }
            baseType = baseType.BaseType;
        }
        return null;
    }
}
