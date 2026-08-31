using System.Text.Json;
using Girder.Abstractions.Hosting;
using Girder.Abstractions.Security.Encryption;
using Girder.Core.Exceptions;
using Girder.Infrastructure.Builder.Modules;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Logging;
using Girder.Infrastructure.Middleware;
using Girder.Infrastructure.Security;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Girder.Infrastructure.Builder;

/// <summary>
/// What Girder can set up, in the order it sets it up.
/// </summary>
/// <remarks>
/// The order is the catalogue's, not the caller's: modules read what earlier
/// ones registered, and two lines swapped in a composition root must not change
/// what a service does.
/// </remarks>
internal static class GirderModuleCatalogue
{
    /// <summary>Every built-in module, in registration order.</summary>
    internal static IReadOnlyList<KeyValuePair<GirderModule, Action<GirderBuilder>>> All { get; } =
    [
        Entry(GirderModule.Logging, girder =>
        {
            LoggingConfiguration.ConfigureSerilog(girder.Configuration, girder.Environment, girder.ServiceName);
            girder.Services.AddSerilog();
        }),

        Entry(GirderModule.HttpContextAccess, girder => girder.Services.AddHttpContextAccessor()),

        Entry(GirderModule.JsonOptions, girder =>
        {
            var indented = girder.Environment.IsDevelopment();

            girder.Services.ConfigureHttpJsonOptions(options => Camel(options.SerializerOptions, indented));
            girder.Services.Configure<JsonOptions>(options => Camel(options.SerializerOptions, indented));
        }),

        Entry(GirderModule.Jwt, girder =>
        {
            // No IJwtService here: it takes a KeyRing, and where the keys come
            // from is what UseJwt(...) answers. Registering it unconditionally
            // would put a consumer in the container whose dependency nothing
            // supplies. These two need no key.
            girder.Services.AddSingleton<ITotpService, TotpService>();
            girder.Services.AddSingleton<IErrorMessageService, ErrorMessageService>();
        }),

        Entry(GirderModule.SecurityMonitoring, girder => Infrastructure(girder).AddSecurityMonitoring()),
        Entry(GirderModule.Resilience, girder => Infrastructure(girder).AddResilience()),
        Entry(GirderModule.SecretManagement, girder => Infrastructure(girder).AddSecretManagement()),
        Entry(GirderModule.Audit, girder => Infrastructure(girder).AddAuditLogging()),
        Entry(GirderModule.InputSanitization, girder => Infrastructure(girder).AddInputSanitization()),
        Entry(GirderModule.RateLimiting, girder => Infrastructure(girder).AddDistributedRateLimiting()),
        Entry(GirderModule.HealthChecks, girder => Infrastructure(girder).AddHealthChecks()),
        Entry(GirderModule.Caching, girder =>
        {
            // Both in-process, and neither needs a decision from anyone: the
            // memory cache several modules read, and the framework's own
            // IDistributedCache. Registering Redis later replaces the latter,
            // because the last registration of a service is the one that wins.
            girder.Services.AddMemoryCache();
            girder.Services.AddDistributedMemoryCache();
        }),
        Entry(GirderModule.Observability, girder => Infrastructure(girder).AddObservability()),
        Entry(GirderModule.SecurityHeaders, girder => Infrastructure(girder).AddSecurityHeaders()),
        // [RequirePermission] names a "Permission:" policy that only
        // PermissionPolicyProvider answers. Needs nothing else, so it belongs in
        // the default set: a module named Authorization that set up the resource
        // half instead would take that machinery away without saying so.
        Entry(GirderModule.Authorization, girder => Infrastructure(girder).AddAuthorization()),

        Entry(GirderModule.CorrelationPropagation, girder => girder.Services.AddCorrelationIdPropagation()),

        Entry(GirderModule.ApiDocumentation, girder =>
        {
            girder.Services.AddEndpointsApiExplorer();
            girder.Services.AddSwaggerDocumentation(girder.ServiceName);
        }),

        Entry(GirderModule.Cors, girder => girder.Services.AddGirderCors(girder.Configuration, girder.Environment)),

        // Below the default line: each needs something the service must supply,
        // and a default that refuses to start is not a default.
        Entry(GirderModule.ResourceAuthorization, girder => Infrastructure(girder).AddResourceAuthorization()),
        Entry(GirderModule.HttpResponseCaching, girder => Infrastructure(girder).AddCaching()),
        Entry(GirderModule.Communication, girder => Infrastructure(girder).AddCommunication()),
        Entry(GirderModule.Encryption, girder => Infrastructure(girder).AddEncryption()),
        Entry(GirderModule.PasswordHashing, girder => Infrastructure(girder).AddPasswordHashing()),
        Entry(GirderModule.TokenSessions, girder => Infrastructure(girder).AddTokenSessions()),
        Entry(GirderModule.Principal, girder => Infrastructure(girder).AddPrincipal())
    ];

    /// <summary>
    /// What <see cref="GirderBuilder.UseDefaults"/> asks for.
    /// </summary>
    /// <remarks>
    /// Everything that is safe without further configuration. Three modules are
    /// deliberately absent, each because it needs a decision Girder must not
    /// make on anyone's behalf: <see cref="GirderModule.HttpResponseCaching"/>
    /// needs a distributed cache, <see cref="GirderModule.Communication"/> needs
    /// a broker, <see cref="GirderModule.Encryption"/> needs a master key, and
    /// <see cref="GirderModule.ResourceAuthorization"/> needs a store to ask
    /// about resources.
    /// Including them would mean a default set that refuses to start, which is
    /// not a default.
    /// </remarks>
    internal static IReadOnlyList<GirderModule> Defaults { get; } =
    [
        GirderModule.Logging,
        GirderModule.HttpContextAccess,
        GirderModule.JsonOptions,
        GirderModule.Jwt,
        GirderModule.SecurityMonitoring,
        GirderModule.Resilience,
        GirderModule.SecretManagement,
        GirderModule.Audit,
        GirderModule.InputSanitization,
        GirderModule.RateLimiting,
        GirderModule.HealthChecks,
        GirderModule.Caching,
        GirderModule.Observability,
        GirderModule.SecurityHeaders,
        GirderModule.Authorization,
        GirderModule.CorrelationPropagation,
        GirderModule.ApiDocumentation,
        GirderModule.Cors
    ];

    private static KeyValuePair<GirderModule, Action<GirderBuilder>> Entry(
        GirderModule module,
        Action<GirderBuilder> register) => new(module, register);

    /// <summary>
    /// A view of the same container for the module extensions that predate this
    /// builder.
    /// </summary>
    private static InfrastructureBuilder Infrastructure(GirderBuilder girder) =>
        new(girder.Services, girder.Configuration, girder.Environment, girder.ServiceName);

    private static void Camel(JsonSerializerOptions options, bool indented)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.WriteIndented = indented;
    }
}
