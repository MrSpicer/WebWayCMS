using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

using WebWayCMS.Attributes;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;
using WebWayCMS.ViewComponents;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsWidgetRegistrationSeeder
{
    internal static WebApplication EnsureWidgetRegistrationsSeeded(this WebApplication app, bool throwOnError = false)
    {
        if (CmsStartupHelpers.IsSkipped("WEBWAYCMS_SKIP_DEFAULTWIDGETS"))
        {
            Log.ForContext(typeof(CmsWidgetRegistrationSeeder)).Information("Skipping widget registration seeding due to WEBWAYCMS_SKIP_DEFAULTWIDGETS=true");
            return app;
        }

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = Log.ForContext(typeof(CmsWidgetRegistrationSeeder));

        try
        {
            var store = services.GetRequiredService<IContentStore<WidgetRegistrationDTO>>();
            var widgetService = services.GetRequiredService<IWidgetRegistrationService>();
            var existing = widgetService.GetActiveAsync().GetAwaiter().GetResult();

            var existingNames = new HashSet<string>(
                existing.Select(w => w.ComponentName),
                StringComparer.OrdinalIgnoreCase);

            var assemblies = new[]
            {
                typeof(ContentZoneViewComponent).Assembly,
                Assembly.GetEntryAssembly()!
            }.Where(a => a != null).Distinct();

            foreach (var assembly in assemblies)
            {
                try
                {
                    SeedAssemblyWidgets(assembly, store, existingNames, logger);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Failed to scan assembly {Assembly} for widget registrations", assembly.FullName);
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error occurred seeding widget registrations.");
            if (throwOnError)
                throw;
        }

        return app;
    }

    private static void SeedAssemblyWidgets(
        Assembly assembly,
        IContentStore<WidgetRegistrationDTO> store,
        HashSet<string> existingNames,
        ILogger logger)
    {
        var viewComponentTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ViewComponent).IsAssignableFrom(t));

        foreach (var type in viewComponentTypes)
        {
            var attribute = type.GetCustomAttribute<ContentZoneComponentAttribute>();
            if (attribute == null)
                continue;

            var componentName = GetWidgetComponentName(type);
            if (existingNames.Contains(componentName))
                continue;

            var propertyDefinitionsJson = "[]";
            if (attribute.ConfigurationType != null)
            {
                try
                {
                    var properties = FormPropertyBuilder.BuildPropertyInfos(attribute.ConfigurationType);
                    propertyDefinitionsJson = JsonSerializer.Serialize(properties);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Failed to build property definitions for widget '{ComponentName}'", componentName);
                }
            }

            var dto = new WidgetRegistrationDTO
            {
                Version = new ContentVersion
                {
                    Title = attribute.DisplayName ?? FormPropertyBuilder.InsertSpaces(componentName),
                    Slug = componentName.ToLowerInvariant(),
                },
                ComponentName = componentName,
                DisplayName = string.IsNullOrEmpty(attribute.DisplayName)
                    ? FormPropertyBuilder.InsertSpaces(componentName)
                    : attribute.DisplayName,
                Description = attribute.Description ?? string.Empty,
                Category = attribute.Category ?? "General",
                IconClass = attribute.IconClass ?? string.Empty,
                Order = attribute.Order,
                ConfigurationTypeName = attribute.ConfigurationType?.FullName,
                PropertyDefinitionsJson = propertyDefinitionsJson,
                IsActive = true,
            };

            try
            {
                var save = store.SaveDraftAsync(dto, null).GetAwaiter().GetResult();
                store.PublishAsync(save.NodeId).GetAwaiter().GetResult();
                existingNames.Add(componentName);
                logger.Information("Seeded widget registration '{ComponentName}' as '{DisplayName}'", componentName, dto.DisplayName);
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to seed widget registration '{ComponentName}'", componentName);
            }
        }
    }

    internal static string GetWidgetComponentName(Type type)
    {
        const string suffix = "ViewComponent";
        var name = type.Name;
        return name.EndsWith(suffix, StringComparison.Ordinal)
            ? name[..^suffix.Length]
            : name;
    }
}
