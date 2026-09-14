using System.Text.Json;
using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security.Encryption;
using Noelia.Core.Exceptions;
using Noelia.Infrastructure.Builder.Modules;
using Noelia.Infrastructure.Extensions;
using Noelia.Infrastructure.Logging;
using Noelia.Infrastructure.Middleware;
using Noelia.Infrastructure.Security;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Noelia.Infrastructure.Builder;

/// <summary>
/// What Noelia can set up, in the order it sets it up.
/// </summary>
/// <remarks>
/// The order is the catalogue's, not the caller's: modules read what earlier
/// ones registered, and two lines swapped in a composition root must not change
/// what a service does.
/// </remarks>
internal static class NoeliaModuleCatalogue
{
    /// <summary>Every built-in module, in registration order.</summary>
    internal static IReadOnlyList<KeyValuePair<NoeliaModule, Action<NoeliaBuilder>>> All { get; } =
    [
        Entry(NoeliaModule.Logging, noelia =>
        {
            LoggingConfiguration.ConfigureSerilog(noelia.Configuration, noelia.Environment, noelia.ServiceName);
            noelia.Services.AddSerilog();
        }),

        Entry(NoeliaModule.HttpContextAccess, noelia => noelia.Services.AddHttpContextAccessor()),

        Entry(NoeliaModule.JsonOptions, noelia =>
        {
            var indented = noelia.Environment.IsDevelopment();

            noelia.Services.ConfigureHttpJsonOptions(options => Camel(options.SerializerOptions, indented));
            noelia.Services.Configure<JsonOptions>(options => Camel(options.SerializerOptions, indented));
        }),

        Entry(NoeliaModule.Jwt, noelia =>
        {
            // No IJwtService here: it takes a KeyRing, and where the keys come
            // from is what UseJwt(...) answers. Registering it unconditionally
            // would put a consumer in the container whose dependency nothing
            // supplies. These two need no key.
            noelia.Services.AddSingleton<ITotpService, TotpService>();
            noelia.Services.AddSingleton<IErrorMessageService, ErrorMessageService>();
        }),

        Entry(NoeliaModule.SecurityMonitoring, noelia => Infrastructure(noelia).AddSecurityMonitoring()),
        Entry(NoeliaModule.Resilience, noelia => Infrastructure(noelia).AddResilience()),
        Entry(NoeliaModule.SecretManagement, noelia => Infrastructure(noelia).AddSecretManagement()),
        Entry(NoeliaModule.Audit, noelia => Infrastructure(noelia).AddAuditLogging()),
        Entry(NoeliaModule.InputSanitization, noelia => Infrastructure(noelia).AddInputSanitization()),
        Entry(NoeliaModule.RateLimiting, noelia => Infrastructure(noelia).AddDistributedRateLimiting()),
        Entry(NoeliaModule.HealthChecks, noelia => Infrastructure(noelia).AddHealthChecks()),
        Entry(NoeliaModule.Caching, noelia =>
        {
            // Both in-process, and neither needs a decision from anyone: the
            // memory cache several modules read, and the framework's own
            // IDistributedCache. Registering Redis later replaces the latter,
            // because the last registration of a service is the one that wins.
            noelia.Services.AddMemoryCache();
            noelia.Services.AddDistributedMemoryCache();
        }),
        Entry(NoeliaModule.Observability, noelia => Infrastructure(noelia).AddObservability()),
        Entry(NoeliaModule.SecurityHeaders, noelia => Infrastructure(noelia).AddSecurityHeaders()),
        // [RequirePermission] names a "Permission:" policy that only
        // PermissionPolicyProvider answers. Needs nothing else, so it belongs in
        // the default set: a module named Authorization that set up the resource
        // half instead would take that machinery away without saying so.
        Entry(NoeliaModule.Authorization, noelia => Infrastructure(noelia).AddAuthorization()),

        // Registers nothing, on purpose. It is the name under which the pipeline
        // step `UsePermissions()` can be left out — without taking the policy
        // provider above with it. A service with any public surface at all needs
        // exactly that separation: the provider answers [RequirePermission] on the
        // endpoints that carry it, the middleware refuses everything it was not
        // told about.
        Entry(NoeliaModule.PermissionEnforcement, _ => { }),

        Entry(NoeliaModule.CorrelationPropagation, noelia => noelia.Services.AddCorrelationIdPropagation()),

        Entry(NoeliaModule.ApiDocumentation, noelia =>
        {
            noelia.Services.AddEndpointsApiExplorer();
            noelia.Services.AddSwaggerDocumentation(noelia.ServiceName);
        }),

        Entry(NoeliaModule.Cors, noelia => noelia.Services.AddNoeliaCors(noelia.Configuration, noelia.Environment)),

        // Below the default line: each needs something the service must supply,
        // and a default that refuses to start is not a default.
        Entry(NoeliaModule.ResourceAuthorization, noelia => Infrastructure(noelia).AddResourceAuthorization()),
        Entry(NoeliaModule.HttpResponseCaching, noelia => Infrastructure(noelia).AddCaching()),
        Entry(NoeliaModule.Communication, noelia => Infrastructure(noelia).AddCommunication()),
        Entry(NoeliaModule.Encryption, noelia => Infrastructure(noelia).AddEncryption()),
        Entry(NoeliaModule.PasswordHashing, noelia => Infrastructure(noelia).AddPasswordHashing()),
        Entry(NoeliaModule.TokenSessions, noelia => Infrastructure(noelia).AddTokenSessions()),
        Entry(NoeliaModule.Principal, noelia => Infrastructure(noelia).AddPrincipal())
    ];

    /// <summary>
    /// What <see cref="NoeliaBuilder.UseDefaults"/> asks for.
    /// </summary>
    /// <remarks>
    /// Everything that is safe without further configuration. Three modules are
    /// deliberately absent, each because it needs a decision Noelia must not
    /// make on anyone's behalf: <see cref="NoeliaModule.HttpResponseCaching"/>
    /// needs a distributed cache, <see cref="NoeliaModule.Communication"/> needs
    /// a broker, <see cref="NoeliaModule.Encryption"/> needs a master key, and
    /// <see cref="NoeliaModule.ResourceAuthorization"/> needs a store to ask
    /// about resources.
    /// Including them would mean a default set that refuses to start, which is
    /// not a default.
    /// </remarks>
    internal static IReadOnlyList<NoeliaModule> Defaults { get; } =
    [
        NoeliaModule.Logging,
        NoeliaModule.HttpContextAccess,
        NoeliaModule.JsonOptions,
        NoeliaModule.Jwt,
        NoeliaModule.SecurityMonitoring,
        NoeliaModule.Resilience,
        NoeliaModule.SecretManagement,
        NoeliaModule.Audit,
        NoeliaModule.InputSanitization,
        NoeliaModule.RateLimiting,
        NoeliaModule.HealthChecks,
        NoeliaModule.Caching,
        NoeliaModule.Observability,
        NoeliaModule.SecurityHeaders,
        NoeliaModule.Authorization,
        NoeliaModule.PermissionEnforcement,
        NoeliaModule.CorrelationPropagation,
        NoeliaModule.ApiDocumentation,
        NoeliaModule.Cors
    ];

    private static KeyValuePair<NoeliaModule, Action<NoeliaBuilder>> Entry(
        NoeliaModule module,
        Action<NoeliaBuilder> register) => new(module, register);

    /// <summary>
    /// A view of the same container for the module extensions that predate this
    /// builder.
    /// </summary>
    private static InfrastructureBuilder Infrastructure(NoeliaBuilder noelia) =>
        new(noelia.Services, noelia.Configuration, noelia.Environment, noelia.ServiceName);

    private static void Camel(JsonSerializerOptions options, bool indented)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.WriteIndented = indented;
    }
}
