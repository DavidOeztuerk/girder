using Microsoft.Extensions.Configuration;

namespace Core.Common.Configuration;

/// <summary>
/// Runtime feature flags for the reversible v1 monetization shutdown.
/// Defaults are intentionally off for production-safe configuration.
/// </summary>
public class MonetizationSettings
{
    public const string SectionName = "Monetization";

    public bool Enabled { get; set; }
    public bool PaymentsEnabled { get; set; }
    public bool BoostsEnabled { get; set; }
    public bool PaidSkillsEnabled { get; set; }

    public bool ArePaymentsEnabled => Enabled && PaymentsEnabled;
    public bool AreBoostsEnabled => Enabled && PaymentsEnabled && BoostsEnabled;
    public bool ArePaidSkillsEnabled => Enabled && PaidSkillsEnabled;

    public static MonetizationSettings FromConfiguration(IConfiguration configuration)
    {
        var settings = new MonetizationSettings
        {
            Enabled = ReadBool(configuration, "Monetization:Enabled", false),
            PaymentsEnabled = ReadBool(configuration, "Monetization:PaymentsEnabled", false),
            BoostsEnabled = ReadBool(configuration, "Monetization:BoostsEnabled", false),
            PaidSkillsEnabled = ReadBool(configuration, "Monetization:PaidSkillsEnabled", false)
        };

        settings.Enabled = ReadBool(configuration, "MONETIZATION_ENABLED", settings.Enabled);
        settings.PaymentsEnabled = ReadBool(configuration, "PAYMENTS_ENABLED", settings.PaymentsEnabled);
        settings.BoostsEnabled = ReadBool(configuration, "BOOSTS_ENABLED", settings.BoostsEnabled);
        settings.PaidSkillsEnabled = ReadBool(configuration, "PAID_SKILLS_ENABLED", settings.PaidSkillsEnabled);

        return settings;
    }

    public static MonetizationSettings EnabledForLegacyBehavior() => new()
    {
        Enabled = true,
        PaymentsEnabled = true,
        BoostsEnabled = true,
        PaidSkillsEnabled = true
    };

    private static bool ReadBool(IConfiguration configuration, string key, bool defaultValue)
    {
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        if (bool.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return raw.Trim() switch
        {
            "1" => true,
            "0" => false,
            _ => defaultValue
        };
    }
}
