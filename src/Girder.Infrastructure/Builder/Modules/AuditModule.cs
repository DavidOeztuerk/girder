using Infrastructure.Security;

namespace Infrastructure.Builder.Modules;

public static class AuditModule
{
    /// <summary>
    /// Add security audit logging (ISecurityAuditLogger).
    /// </summary>
    public static InfrastructureBuilder AddAuditLogging(this InfrastructureBuilder builder)
    {
        builder.AuditEnabled = true;
        builder.Services.AddSecurityAuditLogging();
        return builder;
    }
}
