using Microsoft.Extensions.Hosting;

using NUnit.Framework;

using Serilog;
using Serilog.Core;
using Serilog.Events;

using WebWayCMS.Logging;

namespace WebWayCMS.Host.Tests;

[TestFixture]
public class SerilogExtensionsTests
{
    private static void BuildHostWithSerilog()
    {
        // Building the host executes the UseSerilog configuration callback.
        using var host = new HostBuilder()
            .UseCmsSerilog()
            .Build();
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static ILogger EfCommandLogger(LoggerConfiguration config, bool isDevelopment, CollectingSink sink)
    {
        SerilogExtensions.ApplyMinimumLevelOverrides(config, isDevelopment);
        return config.WriteTo.Sink(sink).CreateLogger()
            .ForContext(Constants.SourceContextPropertyName, "Microsoft.EntityFrameworkCore.Database.Command");
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

        Assert.That(builder.UseCmsSerilog(), Is.SameAs(builder));
    }

    [Test]
    public void ApplyMinimumLevelOverrides_OutsideDevelopment_SilencesEfCommands()
    {
        var sink = new CollectingSink();
        var logger = EfCommandLogger(new LoggerConfiguration(), isDevelopment: false, sink);

        logger.Information("SELECT 1");
        logger.Warning("slow query");

        Assert.Multiple(() =>
        {
            Assert.That(sink.Events, Has.Count.EqualTo(1));
            Assert.That(sink.Events[0].Level, Is.EqualTo(LogEventLevel.Warning));
        });
    }

    [Test]
    public void ApplyMinimumLevelOverrides_InDevelopment_KeepsEfCommands()
    {
        var sink = new CollectingSink();
        var logger = EfCommandLogger(new LoggerConfiguration(), isDevelopment: true, sink);

        logger.Information("SELECT 1");

        Assert.That(sink.Events, Has.Count.EqualTo(1));
    }
}
