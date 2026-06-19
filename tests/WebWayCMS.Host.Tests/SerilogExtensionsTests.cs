using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using NUnit.Framework;

using WebWayCMS.Logging;

namespace WebWayCMS.Host.Tests;

[TestFixture]
public class SerilogExtensionsTests
{
	private static IConfiguration EmptyConfig() =>
		new ConfigurationBuilder().AddInMemoryCollection().Build();

	private static void BuildHostWithSerilog()
	{
		// Building the host executes the UseSerilog configuration callback.
		using var host = new HostBuilder()
			.UseCmsSerilog(EmptyConfig())
			.Build();
	}

	[Test]
	public void UseCmsSerilog_OutsideContainer_ConfiguresFileSink()
	{
		var previous = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
		Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", null);
		try
		{
			Assert.That(BuildHostWithSerilog, Throws.Nothing);
		}
		finally
		{
			Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", previous);
			if (Directory.Exists("Logs"))
				Directory.Delete("Logs", recursive: true);
		}
	}

	[Test]
	public void UseCmsSerilog_InContainer_SkipsFileSink()
	{
		var previous = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
		Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "true");
		try
		{
			Assert.That(BuildHostWithSerilog, Throws.Nothing);
		}
		finally
		{
			Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", previous);
		}
	}

	[Test]
	public void UseCmsSerilog_ReturnsSameBuilderForChaining()
	{
		var builder = new HostBuilder();

		Assert.That(builder.UseCmsSerilog(EmptyConfig()), Is.SameAs(builder));
	}
}
