namespace Girder.Abstractions.Hosting;

/// <summary>
/// The name of one thing Girder can set up.
/// </summary>
/// <remarks>
/// A name and not an enumeration, so a package Girder has never heard of can
/// declare a module of its own and hand it to <c>Use</c>. That is the property
/// that lets a provider extend the composition from outside, the way a database
/// provider extends Entity Framework's options builder.
/// </remarks>
/// <param name="Name">
/// Unique, and readable: it appears in the startup report and in the message a
/// missing provider produces. Prefix a package's own modules
/// (<c>Contoso.Billing</c>) so two packages cannot collide.
/// </param>
public readonly record struct GirderModule(string Name)
{
    /// <summary>Serilog, configured from the service's own configuration.</summary>
    public static GirderModule Logging => new(nameof(Logging));

    /// <summary>Swagger, in development only.</summary>
    public static GirderModule ApiDocumentation => new(nameof(ApiDocumentation));

    /// <summary>Cross-origin rules, from configured origins.</summary>
    public static GirderModule Cors => new(nameof(Cors));

    /// <summary>camelCase in and out, indented while developing.</summary>
    public static GirderModule JsonOptions => new(nameof(JsonOptions));

    /// <summary>The ambient <c>HttpContext</c>, which several modules read.</summary>
    public static GirderModule HttpContextAccess => new(nameof(HttpContextAccess));

    /// <summary>Issuing and reading tokens, and the one-time-code service.</summary>
    public static GirderModule Jwt => new(nameof(Jwt));

    /// <summary>Failed sign-ins, lockouts, and what raises an alarm.</summary>
    public static GirderModule SecurityMonitoring => new(nameof(SecurityMonitoring));

    /// <summary>Circuit breakers and retry policies for outgoing calls.</summary>
    public static GirderModule Resilience => new(nameof(Resilience));

    /// <summary>Reading secrets, and rotating them.</summary>
    public static GirderModule SecretManagement => new(nameof(SecretManagement));

    /// <summary>The security audit trail.</summary>
    public static GirderModule Audit => new(nameof(Audit));

    /// <summary>Encrypting values at rest.</summary>
    public static GirderModule Encryption => new(nameof(Encryption));

    /// <summary>Rejecting or cleaning hostile input.</summary>
    public static GirderModule InputSanitization => new(nameof(InputSanitization));

    /// <summary>Counting requests, and refusing the ones over the line.</summary>
    public static GirderModule RateLimiting => new(nameof(RateLimiting));

    /// <summary>Liveness and readiness, and what they check.</summary>
    public static GirderModule HealthChecks => new(nameof(HealthChecks));

    /// <summary>Calling other services, with caching and deduplication.</summary>
    public static GirderModule Communication => new(nameof(Communication));

    /// <summary>The in-process caches other modules build on.</summary>
    public static GirderModule Caching => new(nameof(Caching));

    /// <summary>
    /// ETags and conditional responses. Needs a distributed cache: an ETag one
    /// instance issues has to be recognised by the next.
    /// </summary>
    public static GirderModule HttpResponseCaching => new(nameof(HttpResponseCaching));

    /// <summary>Traces, metrics and the telemetry pipeline.</summary>
    public static GirderModule Observability => new(nameof(Observability));

    /// <summary>The response headers a browser is told to enforce.</summary>
    public static GirderModule SecurityHeaders => new(nameof(SecurityHeaders));

    /// <summary>Resource-based policies, dormant until an endpoint uses one.</summary>
    public static GirderModule Authorization => new(nameof(Authorization));

    /// <summary>Carrying the correlation id out on every HTTP call.</summary>
    public static GirderModule CorrelationPropagation => new(nameof(CorrelationPropagation));

    /// <summary>Hashing and verifying passwords. Needs a provider package.</summary>
    public static GirderModule PasswordHashing => new(nameof(PasswordHashing));

    /// <summary>Refresh tokens and sessions. Needs a store.</summary>
    public static GirderModule TokenSessions => new(nameof(TokenSessions));

    /// <summary>The current principal, read from the request.</summary>
    public static GirderModule Principal => new(nameof(Principal));

    /// <inheritdoc />
    public override string ToString() => Name;
}
