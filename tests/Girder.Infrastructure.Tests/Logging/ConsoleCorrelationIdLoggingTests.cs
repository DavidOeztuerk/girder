using Girder.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Serilog;
using Serilog.Context;
using Xunit;
using FluentAssertions;

namespace Girder.Infrastructure.Tests.Logging;

[Trait("Category", "Unit")]
public class ConsoleCorrelationIdLoggingTests
{
    [Fact]
    public void Console_log_in_development_contains_correlation_id()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(Environments.Development);

        var originalOut = Console.Out;
        using var stringWriter = new StringWriter();

        try
        {
            Console.SetOut(stringWriter);

            LoggingConfiguration.ConfigureSerilog(config, env, "TestService");

            using (LogContext.PushProperty("CorrelationId", "corr-test-12345"))
            {
                Log.ForContext<ConsoleCorrelationIdLoggingTests>().Information("Message with correlation identifier");
            }

            Log.CloseAndFlush();

            var consoleOutput = stringWriter.ToString();
            consoleOutput.Should().Contain("[corr-test-12345]");
            consoleOutput.Should().Contain("Message with correlation identifier");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
