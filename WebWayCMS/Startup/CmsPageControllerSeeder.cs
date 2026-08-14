using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers;
using WebWayCMS.Controllers.Admin;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsPageControllerSeeder
{
    internal static WebApplication EnsurePageControllerRegistrationsSeeded(this WebApplication app, bool throwOnError = false)
    {
        if (CmsStartupHelpers.IsSkipped("WEBWAYCMS_SKIP_DEFAULTPAGECONTROLLERS"))
        {
            Log.ForContext(typeof(CmsPageControllerSeeder)).Information("Skipping page controller registration seeding due to WEBWAYCMS_SKIP_DEFAULTPAGECONTROLLERS=true");
            return app;
        }

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = Log.ForContext(typeof(CmsPageControllerSeeder));

        try
        {
            var store = services.GetRequiredService<IContentStore<PageControllerRegistrationDTO>>();
            var pageControllerService = services.GetRequiredService<IPageControllerRegistrationService>();
            var existing = pageControllerService.GetActiveAsync().GetAwaiter().GetResult();

            var existingByName = new Dictionary<string, PageControllerRegistrationDTO>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var p in existing)
            {
                existingByName[p.ControllerName] = p;
            }

            var assemblies = new[]
            {
                typeof(GenericPageController).Assembly,
                typeof(AdminContentController).Assembly,
                Assembly.GetEntryAssembly()!
            }.Where(a => a != null).Distinct();

            foreach (var assembly in assemblies)
            {
                try
                {
                    SeedAssemblyPageControllers(assembly, store, existingByName, logger);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Failed to scan assembly {Assembly} for page controller registrations", assembly.FullName);
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error occurred seeding page controller registrations.");
            if (throwOnError)
                throw;
        }

        return app;
    }

    private static void SeedAssemblyPageControllers(
        Assembly assembly,
        IContentStore<PageControllerRegistrationDTO> store,
        Dictionary<string, PageControllerRegistrationDTO> existingByName,
        ILogger logger)
    {
        var controllerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract
                && typeof(Microsoft.AspNetCore.Mvc.Controller).IsAssignableFrom(t)
                && !typeof(Microsoft.AspNetCore.Mvc.ViewComponent).IsAssignableFrom(t));

        foreach (var type in controllerTypes)
        {
            var attribute = type.GetCustomAttribute<PageControllerAttribute>();
            if (attribute == null)
                continue;

            var controllerName = CmsStartupHelpers.GetControllerName(type);

            var attributeConfigType = attribute.ConfigurationType;
            var baseConfigType = ResolveConfigTypeFromBaseClass(type);
            var effectiveConfigType = attributeConfigType ?? baseConfigType;
            if (attributeConfigType != null && baseConfigType != null && attributeConfigType != baseConfigType)
                logger.Warning("Page controller '{ControllerName}' declares ConfigurationType={AttributeType} but its PageControllerBase<T> generic parameter is {BaseType} — they differ.", controllerName, attributeConfigType.FullName, baseConfigType.FullName);

            var propertyDefinitionsJson = "[]";
            if (effectiveConfigType != null)
            {
                try
                {
                    var properties = FormPropertyBuilder.BuildPropertyInfos(effectiveConfigType);
                    propertyDefinitionsJson = JsonSerializer.Serialize(properties);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Failed to build property definitions for page controller '{ControllerName}'", controllerName);
                }
            }

            var expectedConfigTypeName = effectiveConfigType?.FullName;

            if (existingByName.TryGetValue(controllerName, out var existing))
            {
                if (existing.ConfigurationTypeName != expectedConfigTypeName
                    || existing.PropertyDefinitionsJson != propertyDefinitionsJson)
                {
                    var updated = existing with
                    {
                        ConfigurationTypeName = expectedConfigTypeName,
                        PropertyDefinitionsJson = propertyDefinitionsJson
                    };
                    var save = store.SaveDraftAsync(updated, null).GetAwaiter().GetResult();
                    store.PublishAsync(save.NodeId).GetAwaiter().GetResult();
                    logger.Information("Re-synced config metadata for page controller '{ControllerName}'", controllerName);
                }
                continue;
            }

            var dto = new PageControllerRegistrationDTO
            {
                Version = new ContentVersion
                {
                    Title = string.IsNullOrEmpty(attribute.DisplayName)
                        ? FormPropertyBuilder.InsertSpaces(controllerName)
                        : attribute.DisplayName,
                    Slug = controllerName.ToLowerInvariant(),
                },
                ControllerName = controllerName,
                ControllerTypeName = type.FullName ?? type.Name,
                DisplayName = string.IsNullOrEmpty(attribute.DisplayName)
                    ? FormPropertyBuilder.InsertSpaces(controllerName)
                    : attribute.DisplayName,
                Description = attribute.Description ?? string.Empty,
                Category = attribute.Category ?? "General",
                IconClass = attribute.IconClass ?? string.Empty,
                Order = attribute.Order,
                ConfigurationTypeName = expectedConfigTypeName,
                PropertyDefinitionsJson = propertyDefinitionsJson,
                IsActive = true,
            };

            try
            {
                var save = store.SaveDraftAsync(dto, null).GetAwaiter().GetResult();
                store.PublishAsync(save.NodeId).GetAwaiter().GetResult();
                existingByName[controllerName] = dto;
                logger.Information("Seeded page controller registration '{ControllerName}' as '{DisplayName}'", controllerName, dto.DisplayName);
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to seed page controller registration '{ControllerName}'", controllerName);
            }
        }
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
