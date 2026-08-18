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
using WebWayCMS.Models.FormComponentRegistration;
using WebWayCMS.ViewComponents;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsFormComponentSeeder
{
    internal static WebApplication EnsureFormComponentRegistrationsSeeded(this WebApplication app, bool throwOnError = false)
    {
        if (CmsStartupHelpers.IsSkipped("WEBWAYCMS_SKIP_DEFAULTFORMCOMPONENTS"))
        {
            Log.ForContext(typeof(CmsFormComponentSeeder)).Information("Skipping form component registration seeding due to WEBWAYCMS_SKIP_DEFAULTFORMCOMPONENTS=true");
            return app;
        }

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = Log.ForContext(typeof(CmsFormComponentSeeder));

        try
        {
            var formComponentService = services.GetRequiredService<IFormComponentRegistrationService>();
            var existing = formComponentService.GetAllAsync().GetAwaiter().GetResult();

            var existingByComponentName = new Dictionary<string, FormComponentRegistrationDTO>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var f in existing)
            {
                existingByComponentName[f.ComponentName] = f;
            }

            var assemblies = CmsStartupHelpers.SeedAssemblies(
                services,
                typeof(ContentZoneViewComponent).Assembly,
                typeof(FormComponentRegistrationModel).Assembly);

            foreach (var assembly in assemblies)
            {
                try
                {
                    SeedAssemblyFormComponents(assembly, formComponentService, existingByComponentName, logger);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Failed to scan assembly {Assembly} for form component registrations", assembly.FullName);
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error occurred seeding form component registrations.");
            if (throwOnError)
                throw;
        }

        return app;
    }

    private static void SeedAssemblyFormComponents(
        Assembly assembly,
        IFormComponentRegistrationService service,
        Dictionary<string, FormComponentRegistrationDTO> existingByComponentName,
        ILogger logger)
    {
        var viewComponentTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ViewComponent).IsAssignableFrom(t));

        foreach (var type in viewComponentTypes)
        {
            var attribute = type.GetCustomAttribute<CMSFormComponentAttribute>();
            if (attribute == null)
                continue;

            var componentName = string.IsNullOrEmpty(attribute.Name)
                ? GetFormComponentName(type)
                : attribute.Name;

            var viewComponentName = type.Name;

            var writeViewName = attribute.WriteViewName;
            var readViewName = attribute.ReadViewName;

            var dataTypeNamesJson = JsonSerializer.Serialize(
                (attribute.DataTypes ?? Array.Empty<Type>()).Select(t => t.FullName));
            var editorTypeAlias = attribute.EditorType?.ToString();

            if (existingByComponentName.TryGetValue(componentName, out var existing))
            {
                if (existing.DataTypeNamesJson != dataTypeNamesJson
                    || existing.EditorTypeAlias != editorTypeAlias
                    || existing.IsDefaultForType != attribute.IsDefaultForType
                    || existing.WriteViewName != writeViewName
                    || existing.ReadViewName != readViewName
                    || existing.ViewComponentName != viewComponentName)
                {
                    existing.DataTypeNamesJson = dataTypeNamesJson;
                    existing.EditorTypeAlias = editorTypeAlias;
                    existing.IsDefaultForType = attribute.IsDefaultForType;
                    existing.WriteViewName = writeViewName;
                    existing.ReadViewName = readViewName;
                    existing.ViewComponentName = viewComponentName;

                    var save = service.UpsertAsync(existing).GetAwaiter().GetResult();
                    if (save.Success)
                        logger.Information("Re-synced form component registration '{ComponentName}'", componentName);
                    else
                        logger.Warning("Failed to re-sync form component registration '{ComponentName}': {Error}", componentName, save.ErrorMessage);
                }
                continue;
            }

            var dto = new FormComponentRegistrationDTO
            {
                ComponentName = componentName,
                ViewComponentName = viewComponentName,
                DisplayName = string.IsNullOrEmpty(attribute.DisplayName)
                    ? FormPropertyBuilder.InsertSpaces(componentName)
                    : attribute.DisplayName,
                Description = attribute.Description ?? string.Empty,
                Category = attribute.Category ?? "General",
                IconClass = attribute.IconClass ?? string.Empty,
                Order = attribute.Order,
                DataTypeNamesJson = dataTypeNamesJson,
                EditorTypeAlias = editorTypeAlias,
                IsDefaultForType = attribute.IsDefaultForType,
                WriteViewName = writeViewName,
                ReadViewName = readViewName,
                IsActive = true,
            };

            try
            {
                var save = service.UpsertAsync(dto).GetAwaiter().GetResult();
                if (save.Success)
                {
                    existingByComponentName[componentName] = dto;
                    logger.Information("Seeded form component registration '{ComponentName}' as '{DisplayName}'", componentName, dto.DisplayName);
                }
                else
                {
                    logger.Warning("Failed to seed form component registration '{ComponentName}': {Error}", componentName, save.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to seed form component registration '{ComponentName}'", componentName);
            }
        }
    }

    internal static string GetFormComponentName(Type type)
    {
        const string suffix = "ViewComponent";
        var name = type.Name;
        return name.EndsWith(suffix, StringComparison.Ordinal)
            ? name[..^suffix.Length]
            : name;
    }
}
