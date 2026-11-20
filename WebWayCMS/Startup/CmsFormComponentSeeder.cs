using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

using WebWayCMS.Attributes;
using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Data.Services.Registrations;
using WebWayCMS.Forms;
using WebWayCMS.Models.FormComponentRegistration;
using WebWayCMS.ViewComponents;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsFormComponentSeeder
{
    /// <summary>
    /// One registration per <c>[CMSFormComponent]</c> view component. The data types, editor type alias,
    /// default-for-type flag and view component name are code-owned, kept in line on every boot.
    /// </summary>
    internal static WebApplication EnsureFormComponentRegistrationsSeeded(this WebApplication app, bool throwOnError = false)
        => app.EnsureRegistrationsSynced(
            "form component",
            "WEBWAYCMS_SKIP_DEFAULTFORMCOMPONENTS",
            throwOnError,
            services => new FormComponentRegistrationSyncTarget(
                services.GetRequiredService<CmsDbContext>(),
                services.GetRequiredService<IFormComponentRegistrationService>()),
            (services, logger) => Declared(
                CmsStartupHelpers.SeedAssemblies(
                    services,
                    typeof(ContentZoneViewComponent).Assembly,
                    typeof(FormComponentRegistrationModel).Assembly),
                logger));

    private static IEnumerable<RegistrationSyncItem<FormComponentRegistrationDTO>> Declared(IEnumerable<Assembly> assemblies, ILogger logger)
    {
        foreach (var (type, attribute) in CmsRegistrationSync.TypesWith<CMSFormComponentAttribute>(
                     assemblies, t => typeof(ViewComponent).IsAssignableFrom(t), logger))
        {
            var componentName = string.IsNullOrEmpty(attribute.Name)
                ? GetFormComponentName(type)
                : attribute.Name;
            var viewComponentName = type.Name;
            var dataTypeNamesJson = JsonSerializer.Serialize(
                (attribute.DataTypes ?? Array.Empty<Type>()).Select(t => t.FullName));
            var editorTypeAlias = attribute.EditorType?.ToString();
            var isDefaultForType = attribute.IsDefaultForType;

            yield return new RegistrationSyncItem<FormComponentRegistrationDTO>(
                componentName,
                () => new FormComponentRegistrationDTO
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
                    IsDefaultForType = isDefaultForType,
                    IsActive = true,
                },
                row =>
                {
                    if (row.DataTypeNamesJson == dataTypeNamesJson
                        && row.EditorTypeAlias == editorTypeAlias
                        && row.IsDefaultForType == isDefaultForType
                        && row.ViewComponentName == viewComponentName)
                        return false;
                    row.DataTypeNamesJson = dataTypeNamesJson;
                    row.EditorTypeAlias = editorTypeAlias;
                    row.IsDefaultForType = isDefaultForType;
                    row.ViewComponentName = viewComponentName;
                    return true;
                });
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
