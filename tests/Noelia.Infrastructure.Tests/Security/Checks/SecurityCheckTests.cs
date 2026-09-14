using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security;
using Noelia.Abstractions.Security.Checks;
using Noelia.Abstractions.Security.Encryption;
using Noelia.Infrastructure.Models;
using Noelia.Infrastructure.Security.Checks;
using Noelia.Infrastructure.Security.Headers;
using Noelia.Infrastructure.Security.Keys;

namespace Noelia.Infrastructure.Tests.Security.Checks;

[Trait("Category", "Unit")]
public class SecurityCheckTests
{
    private static readonly NoeliaModule Active = new("Test.Active");
    private const string Canary = "postgres://admin:secret@example.invalid/token-value";

    [Fact]
    public async Task Missing_declared_provider_fails_the_composition_check()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
        var contract = new NoeliaModuleContract(
            Active,
            [new NoeliaServiceRequirement(
                typeof(Noelia.Abstractions.Security.Secrets.ISecretProvider),
                [new NoeliaProviderHint("Noelia.InMemory", "AddInMemorySecretProvider()")])],
            []);
        var composition = new NoeliaComposition(
            [Active],
            new Dictionary<NoeliaModule, string>(),
            new Dictionary<NoeliaModule, NoeliaModuleContract> { [Active] = contract });
        var check = new CompositionSecurityCheck(provider, composition);

        (await check.RunAsync()).Status.Should().Be(SecurityCheckStatus.Fail);
    }

    [Fact]
    public async Task Runner_executes_only_checks_for_active_modules()
    {
        var active = new ProbeCheck("test.active", Active);
        var inactive = new ProbeCheck("test.inactive", new NoeliaModule("Test.Inactive"));
        var runner = Runner([active, inactive]);

        var results = await runner.RunAsync();

        results.Should().ContainSingle(result => result.Id == active.Id);
        active.Calls.Should().Be(1);
        inactive.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Runner_rejects_an_unstable_check_id()
    {
        var runner = Runner([new ProbeCheck("Not Stable", Active)]);

        await FluentActions.Invoking(() => runner.RunAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*stable lowercase dotted id*");
    }

    [Fact]
    public async Task Runner_rejects_duplicate_check_ids()
    {
        var runner = Runner(
        [
            new ProbeCheck("test.duplicate", Active),
            new ProbeCheck("test.duplicate", Active)
        ]);

        await FluentActions.Invoking(() => runner.RunAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*registered twice*");
    }

    [Fact]
    public async Task Exception_text_never_reaches_result_or_log()
    {
        var logger = new RecordingLogger();
        var runner = Runner([new ThrowingCheck()], logger: logger);

        var results = await runner.RunAsync();

        string.Join(" ", results.Select(result => $"{result.Summary} {result.Remediation}"))
            .Should().NotContain(Canary);
        string.Join(" ", logger.Messages).Should().NotContain(Canary);
    }

    [Fact]
    public async Task Timeout_cancels_a_hung_check_and_returns_a_value_free_failure()
    {
        var check = new HangingCheck();
        var runner = Runner([check], TimeSpan.FromMilliseconds(25));

        var results = await runner.RunAsync();

        results.Single().Status.Should().Be(SecurityCheckStatus.Fail);
        await check.CancellationObserved.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Shared_jwt_secret_fails_outside_development()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new KeyRing(
            [SigningKey.FromSharedSecret(new string('s', 32), "shared")],
            SigningKey.FromSharedSecret(new string('s', 32), "shared")));
        var provider = services.BuildServiceProvider();
        var check = new JwtSecurityCheck(provider, new EnvironmentStub(Environments.Production));

        (await check.RunAsync()).Status.Should().Be(SecurityCheckStatus.Fail);
    }

    [Fact]
    public async Task Disabled_security_headers_fail()
    {
        var check = new SecurityHeadersSecurityCheck(
            Options.Create(new SecurityHeadersOptions { EnableDefaultCsp = false }),
            Options.Create(new SecurityHeadersMiddlewareOptions()),
            new EnvironmentStub(Environments.Production));

        (await check.RunAsync()).Status.Should().Be(SecurityCheckStatus.Fail);
    }

    [Fact]
    public async Task Insecure_cookie_fails_without_echoing_its_name_or_value()
    {
        var services = new ServiceCollection();
        services.AddAuthentication().AddCookie("unsafe-cookie", options =>
        {
            options.Cookie.Name = Canary;
            options.Cookie.HttpOnly = false;
            options.Cookie.SecurePolicy = CookieSecurePolicy.None;
        });
        var check = new RefreshCookieSecurityCheck(services.BuildServiceProvider());

        var result = await check.RunAsync();

        result.Status.Should().Be(SecurityCheckStatus.Fail);
        $"{result.Summary} {result.Remediation}".Should().NotContain(Canary);
    }

    [Fact]
    public async Task Wildcard_cors_origin_fails()
    {
        var cors = new CorsOptions();
        cors.AddDefaultPolicy(policy => policy.AllowAnyOrigin());
        var check = new CorsSecurityCheck(
            Options.Create(cors),
            new EnvironmentStub(Environments.Production));

        (await check.RunAsync()).Status.Should().Be(SecurityCheckStatus.Fail);
    }

    [Fact]
    public async Task Missing_secret_provider_fails()
    {
        var check = new SecretProviderSecurityCheck(
            new ServiceCollection().BuildServiceProvider(),
            new EnvironmentStub(Environments.Production));

        (await check.RunAsync()).Status.Should().Be(SecurityCheckStatus.Fail);
    }

    [Fact]
    public async Task Unauthenticated_encryption_algorithm_fails()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDataEncryptionService>());
        var key = Substitute.For<IMasterKeyProvider>();
        key.GetMasterKey().Returns(new byte[32]);
        services.AddSingleton(key);
        var check = new EncryptionSecurityCheck(
            services.BuildServiceProvider(),
            Options.Create(new DataEncryptionOptions { DefaultAlgorithm = EncryptionAlgorithm.AES256CBC }));

        (await check.RunAsync()).Status.Should().Be(SecurityCheckStatus.Fail);
    }

    [Fact]
    public async Task Fail_open_rate_limit_fails()
    {
        var options = new DistributedRateLimitingOptions();
        options.CircuitBreaker.FallbackBehavior = CircuitBreakerFallback.AllowAll;
        var check = new RateLimitDegradationSecurityCheck(
            new ServiceCollection().BuildServiceProvider(),
            Options.Create(options),
            new EnvironmentStub(Environments.Production));

        (await check.RunAsync()).Status.Should().Be(SecurityCheckStatus.Fail);
    }

    [Fact]
    public async Task Explicitly_disabled_revocation_fails_in_production()
    {
        var services = new ServiceCollection();
        services.AddNoTokenRevocation("access tokens expire quickly");
        var check = new RevocationDegradationSecurityCheck(
            services.BuildServiceProvider(),
            new EnvironmentStub(Environments.Production));

        (await check.RunAsync()).Status.Should().Be(SecurityCheckStatus.Fail);
    }

    private static SecurityCheckRunner Runner(
        IEnumerable<ISecurityCheck> checks,
        TimeSpan? timeout = null,
        ILogger<SecurityCheckRunner>? logger = null)
    {
        var contract = new NoeliaModuleContract(Active, [], []);
        var composition = new NoeliaComposition(
            [Active],
            new Dictionary<NoeliaModule, string>(),
            new Dictionary<NoeliaModule, NoeliaModuleContract> { [Active] = contract });
        return new SecurityCheckRunner(
            checks,
            composition,
            Options.Create(new SecurityCheckOptions { Timeout = timeout ?? TimeSpan.FromSeconds(1) }),
            new SecurityCheckReport(),
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SecurityCheckRunner>.Instance);
    }

    private sealed class ProbeCheck(string id, NoeliaModule module) : ISecurityCheck
    {
        public int Calls { get; private set; }
        public string Id => id;
        public NoeliaModule Module => module;
        public SecurityCheckCategory Category => SecurityCheckCategory.Secrets;
        public SecurityCheckSeverity Severity => SecurityCheckSeverity.Low;
        public string Remediation => "None.";

        public Task<SecurityCheckResult> RunAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new SecurityCheckResult(
                Id, Module, Category, SecurityCheckStatus.Pass, Severity, "Safe.", Remediation));
        }
    }

    private sealed class ThrowingCheck : ISecurityCheck
    {
        public string Id => "test.throwing";
        public NoeliaModule Module => Active;
        public SecurityCheckCategory Category => SecurityCheckCategory.Secrets;
        public SecurityCheckSeverity Severity => SecurityCheckSeverity.High;
        public string Remediation => "Use the documented provider.";
        public Task<SecurityCheckResult> RunAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(Canary);
    }

    private sealed class HangingCheck : ISecurityCheck
    {
        private readonly TaskCompletionSource _cancellationObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task CancellationObserved => _cancellationObserved.Task;
        public string Id => "test.hanging";
        public NoeliaModule Module => Active;
        public SecurityCheckCategory Category => SecurityCheckCategory.Secrets;
        public SecurityCheckSeverity Severity => SecurityCheckSeverity.High;
        public string Remediation => "Inspect the provider.";

        public async Task<SecurityCheckResult> RunAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _cancellationObserved.TrySetResult();
                throw;
            }

            throw new UnreachableException();
        }
    }

    private sealed class EnvironmentStub(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "security-check-test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class RecordingLogger : ILogger<SecurityCheckRunner>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (exception is not null)
            {
                Messages.Add(exception.ToString());
            }
        }
    }
}
