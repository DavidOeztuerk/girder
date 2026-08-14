// namespace Contracts.Common;

// /// <summary>
// /// Attribute to mark contract versions and deprecation status
// /// </summary>
// [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
// public class ContractVersionAttribute : Attribute
// {
//     /// <summary>
//     /// The version of this contract
//     /// </summary>
//     public string Version { get; }

//     /// <summary>
//     /// Whether this contract version is deprecated
//     /// </summary>
//     public bool IsDeprecated { get; set; }

//     /// <summary>
//     /// The version this contract is deprecated in favor of
//     /// </summary>
//     public string? DeprecatedInFavorOf { get; set; }

//     /// <summary>
//     /// When this contract version will be removed
//     /// </summary>
//     public DateTime? DeprecationDate { get; set; }

//     /// <summary>
//     /// Description of changes in this version
//     /// </summary>
//     public string? ChangeDescription { get; set; }

//     public ContractVersionAttribute(string version)
//     {
//         Version = version;
//     }
// }

// /// <summary>
// /// Attribute to mark breaking changes in contracts
// /// </summary>
// [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
// public class BreakingChangeAttribute : Attribute
// {
//     /// <summary>
//     /// Version where the breaking change was introduced
//     /// </summary>
//     public string Version { get; }

//     /// <summary>
//     /// Description of the breaking change
//     /// </summary>
//     public string Description { get; set; } = string.Empty;

//     /// <summary>
//     /// Migration instructions
//     /// </summary>
//     public string? MigrationInstructions { get; set; }

//     public BreakingChangeAttribute(string version)
//     {
//         Version = version;
//     }
// }