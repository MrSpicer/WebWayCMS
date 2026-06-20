using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

using WebWayCMS.Presentation.Rendering;

namespace WebWayCMS.Presentation.Tests;

/// <summary>
/// Renders a Razor component to a static HTML string using the in-framework
/// <see cref="HtmlRenderer"/> (no external test dependency such as bUnit). Used to cover the
/// markup branches of the CMS Razor components.
/// </summary>
internal static class BlazorRenderHarness
{
	public static async Task<string> RenderAsync<TComponent>(
		IDictionary<string, object?>? parameters = null,
		Action<IServiceCollection>? configureServices = null)
		where TComponent : IComponent
	{
		var services = new ServiceCollection();
		services.AddLogging();
		// HeadOutlet (used by the document shell) requires IJSRuntime for property injection even
		// under static SSR, where it is never actually invoked. A no-op satisfies the injection.
		services.TryAddSingleton<IJSRuntime>(new NoOpJSRuntime());
		configureServices?.Invoke(services);
		await using var provider = services.BuildServiceProvider();
		var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
		await using var renderer = new HtmlRenderer(provider, loggerFactory);

		return await renderer.Dispatcher.InvokeAsync(async () =>
		{
			var view = parameters is null ? ParameterView.Empty : ParameterView.FromDictionary(parameters);
			var output = await renderer.RenderComponentAsync<TComponent>(view);
			return output.ToHtmlString();
		});
	}

	/// <summary>
	/// Renders <typeparamref name="TComponent"/> wrapped in a <see cref="CmsRenderContext"/> cascade so
	/// components that read the cascaded context (page, sub-route) can be exercised with it present.
	/// </summary>
	public static Task<string> RenderInContextAsync<TComponent>(
		CmsRenderContext context,
		IDictionary<string, object?>? parameters = null,
		Action<IServiceCollection>? configureServices = null)
		where TComponent : IComponent
	{
		RenderFragment child = builder =>
		{
			builder.OpenComponent<TComponent>(0);
			if (parameters is not null)
			{
				var seq = 1;
				foreach (var kvp in parameters)
					builder.AddComponentParameter(seq++, kvp.Key, kvp.Value);
			}
			builder.CloseComponent();
		};

		var rootParameters = new Dictionary<string, object?>
		{
			["Value"] = context,
			["IsFixed"] = true,
			["ChildContent"] = child,
		};

		return RenderAsync<CascadingValue<CmsRenderContext>>(rootParameters, configureServices);
	}

	private sealed class NoOpJSRuntime : IJSRuntime
	{
		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
			=> throw new NotSupportedException("JS interop is not available during static rendering.");

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
			=> throw new NotSupportedException("JS interop is not available during static rendering.");
	}
}
