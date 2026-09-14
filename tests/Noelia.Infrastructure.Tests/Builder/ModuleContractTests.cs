using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Noelia.Abstractions.Hosting;
using Noelia.Infrastructure.Extensions;

namespace Noelia.Infrastructure.Tests.Builder;

[Trait("Category", "Unit")]
public class ModuleContractTests
{
    private static readonly NoeliaModule Consumer = new("Test.Consumer");
    private static readonly NoeliaModule BrokenProvider = new("Test.BrokenProvider");
    private static readonly NoeliaModule LateSelection = new("Test.LateSelection");

    [Fact]
    public void Missing_requirement_names_service_module_package_and_registration()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddNoelia(
            builder.Configuration,
            builder.Environment,
            "contract-test",
            noelia => noelia.Use(
                Consumer,
                _ => { },
                contract => contract.Requires<ITestPort>(
                    new NoeliaProviderHint("Noelia.TestProvider", "AddTestProvider()"))));

        var app = builder.Build();
        var start = () => RunStartupFilters(app);

        start.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain(nameof(ITestPort))
            .And.Contain("Test.Consumer")
            .And.Contain("Noelia.TestProvider")
            .And.Contain("AddTestProvider()");
    }

    [Fact]
    public void Present_requirement_allows_startup()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddNoelia(
            builder.Configuration,
            builder.Environment,
            "contract-test",
            noelia => noelia.Use(
                Consumer,
                _ => { },
                contract => contract.Requires<ITestPort>(
                    new NoeliaProviderHint("Noelia.TestProvider", "AddTestProvider()"))));
        builder.Services.AddSingleton<ITestPort, TestPort>();

        var app = builder.Build();

        ((Action)(() => RunStartupFilters(app))).Should().NotThrow();
    }

    [Fact]
    public void A_module_that_breaks_its_provides_promise_is_rejected_immediately()
    {
        var services = new ServiceCollection();

        var compose = () => services.AddNoelia(
            new ConfigurationBuilder().Build(),
            new EnvironmentStub(),
            "contract-test",
            noelia => noelia.Use(
                BrokenProvider,
                _ => { },
                contract => contract.Provides<ITestPort>(
                    "Noelia.Broken", "AddBrokenProvider()")));

        compose.Should().Throw<InvalidOperationException>()
            .WithMessage("*Test.BrokenProvider*ITestPort*");
    }

    [Fact]
    public void Excluded_module_neither_registers_nor_appears_as_active_contract()
    {
        var ran = false;
        var services = new ServiceCollection();
        services.AddNoelia(
            new ConfigurationBuilder().Build(),
            new EnvironmentStub(),
            "contract-test",
            noelia => noelia
                .Use(Consumer, _ => ran = true, _ => { })
                .Without(Consumer, "provided elsewhere"));

        var composition = services.BuildServiceProvider().GetRequiredService<NoeliaComposition>();

        ran.Should().BeFalse();
        composition.Included.Should().NotContain(Consumer);
        composition.Contracts.Should().NotContainKey(Consumer);
        composition.Excluded.Should().ContainKey(Consumer);
    }

    [Fact]
    public void A_registration_cannot_change_the_composition_after_it_was_frozen()
    {
        var services = new ServiceCollection();

        var compose = () => services.AddNoelia(
            new ConfigurationBuilder().Build(),
            new EnvironmentStub(),
            "contract-test",
            noelia => noelia.Use(
                Consumer,
                active => active.Use(LateSelection, _ => { }, _ => { }),
                _ => { }));

        compose.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot select or exclude another module*");
    }

    [Fact]
    public void Composition_is_a_snapshot_of_the_sources_that_created_it()
    {
        var included = new List<NoeliaModule> { Consumer };
        var excluded = new Dictionary<NoeliaModule, string> { [LateSelection] = "not needed" };
        var contracts = new Dictionary<NoeliaModule, NoeliaModuleContract>
        {
            [Consumer] = new(Consumer, [], [])
        };

        var composition = new NoeliaComposition(included, excluded, contracts);
        included.Clear();
        excluded.Clear();
        contracts.Clear();

        composition.Included.Should().Equal(Consumer);
        composition.Excluded.Should().ContainKey(LateSelection);
        composition.Contracts.Should().ContainKey(Consumer);
    }

    private static void RunStartupFilters(WebApplication app)
    {
        foreach (var filter in app.Services.GetServices<IStartupFilter>())
        {
            filter.Configure(_ => { })(app);
        }
    }

    private interface ITestPort;
    private sealed class TestPort : ITestPort;

    private sealed class EnvironmentStub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "contract-test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
