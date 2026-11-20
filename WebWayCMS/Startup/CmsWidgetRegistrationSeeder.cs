using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

using WebWayCMS.Attributes;
using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Data.Services.Registrations;
using WebWayCMS.Forms;
using WebWayCMS.ViewComponents;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsWidgetRegistrationSeeder
{
    /// <summary>
    /// One registration per <c>[ContentZoneComponent]</c> view component. <see cref="WidgetRegistrationDTO.SupportsList"/>
    /// is a code capability, so it is the code-owned column kept in line on every boot.
    /// </summary>
    internal static WebApplication EnsureWidgetRegistrationsSeeded(this WebApplication app, bool throwOnError = false)
        => app.EnsureRegistrationsSynced(
            "widget",
            "WEBWAYCMS_SKIP_DEFAULTWIDGETS",
            throwOnError,
            services => new VersionedRegistrationSyncTarget<WidgetRegistrationDTO>(
                services.GetRequiredService<CmsDbContext>(),
                services.GetRequiredService<IContentStore<WidgetRegistrationDTO>>(),
                w => w.ComponentName),
            (services, logger) => Declared(
                CmsStartupHelpers.SeedAssemblies(services, typeof(ContentZoneViewComponent).Assembly), logger));

    private static IEnumerable<RegistrationSyncItem<WidgetRegistrationDTO>> Declared(IEnumerable<Assembly> assemblies, ILogger logger)
    {
        foreach (var (type, attribute) in CmsRegistrationSync.TypesWith<ContentZoneComponentAttribute>(
                     assemblies, t => typeof(ViewComponent).IsAssignableFrom(t), logger))
        {
            var componentName = GetWidgetComponentName(type);
            var supportsList = attribute.SupportsList;
            yield return new RegistrationSyncItem<WidgetRegistrationDTO>(
                componentName,
                () => Create(componentName, attribute, logger),
                row =>
                {
                    if (row.SupportsList == supportsList)
                        return false;
                    row.SupportsList = supportsList;
                    return true;
                });
        }
    }

    private static WidgetRegistrationDTO Create(string componentName, ContentZoneComponentAttribute attribute, ILogger logger)
    {
        var propertyDefinitionsJson = PropertyDefinitionsJson.Empty;
        if (attribute.ConfigurationType != null)
        {
            try
            {
                propertyDefinitionsJson = PropertyDefinitionsJson.Build(attribute.ConfigurationType);
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to build property definitions for widget '{ComponentName}'", componentName);
            }
        }

        var displayName = string.IsNullOrEmpty(attribute.DisplayName)
            ? FormPropertyBuilder.InsertSpaces(componentName)
            : attribute.DisplayName;

        return new WidgetRegistrationDTO
        {
            Version = new ContentVersion
            {
                Title = displayName,
                Slug = componentName.ToLowerInvariant(),
            },
            ComponentName = componentName,
            DisplayName = displayName,
            Description = attribute.Description ?? string.Empty,
            Category = attribute.Category ?? "General",
            IconClass = attribute.IconClass ?? string.Empty,
            Order = attribute.Order,
            ConfigurationTypeName = attribute.ConfigurationType?.FullName,
            PropertyDefinitionsJson = propertyDefinitionsJson,
            SupportsList = attribute.SupportsList,
            IsActive = true,
        };
    }

    /// <summary>
    /// The name MVC invokes the component by (and searches its views under):
    /// <c>[ViewComponent(Name = ...)]</c> when present, else the type name minus "ViewComponent".
    /// </summary>
    internal static string GetWidgetComponentName(Type type)
        => ViewComponentConventions.GetComponentName(type.GetTypeInfo());
}
