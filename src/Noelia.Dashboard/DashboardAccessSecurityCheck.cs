using Microsoft.Extensions.DependencyInjection;
using Noelia.Abstractions.Audit;
using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security.Checks;

namespace Noelia.Dashboard;

internal sealed class DashboardAccessSecurityCheck(
    NoeliaDashboardOptions options,
    IServiceProviderIsService services) : ISecurityCheck
{
    public string Id => "noelia.dashboard.operator-access";

    public NoeliaModule Module => NoeliaModule.Dashboard;

    public SecurityCheckCategory Category => SecurityCheckCategory.Composition;

    public SecurityCheckSeverity Severity => SecurityCheckSeverity.High;

    public string Remediation =>
        "Configure an explicit operator policy and an audit trail before exposing the dashboard.";

    public Task<SecurityCheckResult> RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var policyConfigured = options.Visibility is not null;
        var auditConfigured = services.IsService(typeof(IAuditTrailService));
        var status = policyConfigured && auditConfigured
            ? SecurityCheckStatus.Pass
            : SecurityCheckStatus.Warning;

        var summary = (policyConfigured, auditConfigured) switch
        {
            (true, true) => "An explicit operator policy and audit trail protect dashboard access.",
            (false, _) => "No operator policy is configured; dashboard requests remain indistinguishable 404 responses.",
            _ => "An operator policy is configured, but dashboard access cannot be written to an audit trail."
        };

        return Task.FromResult(new SecurityCheckResult(
            Id, Module, Category, status, Severity, summary, Remediation));
    }
}
