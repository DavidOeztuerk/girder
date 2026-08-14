// using System.Reflection;

// namespace Contracts.Common;

// /// <summary>
// /// Service for migrating contracts between versions
// /// </summary>
// public class ContractMigrationService
// {
//     private readonly Dictionary<(Type FromType, Type ToType), Func<object, object>> _migrationMap = new();

//     public ContractMigrationService()
//     {
//         RegisterMigrations();
//     }

//     /// <summary>
//     /// Migrates a contract from one version to another
//     /// </summary>
//     /// <typeparam name="TFrom">Source contract type</typeparam>
//     /// <typeparam name="TTo">Target contract type</typeparam>
//     /// <param name="source">Source contract instance</param>
//     /// <returns>Migrated contract</returns>
//     public TTo? Migrate<TFrom, TTo>(TFrom source)
//         where TFrom : IVersionedContract
//         where TTo : IVersionedContract
//     {
//         if (source == null) return default;

//         var key = (typeof(TFrom), typeof(TTo));
//         if (_migrationMap.TryGetValue(key, out var migrationFunc))
//         {
//             return (TTo)migrationFunc(source);
//         }

//         throw new InvalidOperationException($"No migration found from {typeof(TFrom).Name} to {typeof(TTo).Name}");
//     }

//     /// <summary>
//     /// Checks if migration is supported between two contract types
//     /// </summary>
//     public bool CanMigrate<TFrom, TTo>()
//         where TFrom : IVersionedContract
//         where TTo : IVersionedContract
//     {
//         return _migrationMap.ContainsKey((typeof(TFrom), typeof(TTo)));
//     }

//     /// <summary>
//     /// Gets the latest version of a contract type
//     /// </summary>
//     public string? GetLatestVersion<T>() where T : IVersionedContract
//     {
//         var contractTypes = GetContractVersions<T>();
//         return contractTypes.Max(kvp => kvp.Key);
//     }

//     /// <summary>
//     /// Gets all versions of a contract type
//     /// </summary>
//     public Dictionary<string, Type> GetContractVersions<T>() where T : IVersionedContract
//     {
//         var baseType = typeof(T);
//         var assembly = baseType.Assembly;
//         var versions = new Dictionary<string, Type>();

//         foreach (var type in assembly.GetTypes())
//         {
//             if (type.IsAssignableTo(typeof(IVersionedContract)))
//             {
//                 var versionAttr = type.GetCustomAttribute<ContractVersionAttribute>();
//                 if (versionAttr != null)
//                 {
//                     versions[versionAttr.Version] = type;
//                 }
//                 else if (type.IsAssignableTo(baseType))
//                 {
//                     // Try to get version from instance
//                     try
//                     {
//                         var instance = Activator.CreateInstance(type) as IVersionedContract;
//                         if (instance != null)
//                         {
//                             versions[instance.ApiVersion] = type;
//                         }
//                     }
//                     catch
//                     {
//                         // Ignore types that can't be instantiated
//                     }
//                 }
//             }
//         }

//         return versions;
//     }

//     private void RegisterMigrations()
//     {
//         // Auto-discover migrations from assemblies
//         var assemblies = AppDomain.CurrentDomain.GetAssemblies();

//         foreach (var assembly in assemblies)
//         {
//             foreach (var type in assembly.GetTypes())
//             {
//                 RegisterMigrationsForType(type);
//             }
//         }
//     }

//     private void RegisterMigrationsForType(Type type)
//     {
//         var migratableInterfaces = type.GetInterfaces()
//             .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMigratableContract<>));

//         foreach (var migratableInterface in migratableInterfaces)
//         {
//             var previousType = migratableInterface.GetGenericArguments()[0];
//             var migrateMethod = type.GetMethod("MigrateFrom", BindingFlags.Static | BindingFlags.Public);

//             if (migrateMethod != null)
//             {
//                 _migrationMap[(previousType, type)] = source => migrateMethod.Invoke(null, [source])!;
//             }
//         }

//         var backwardCompatibleInterfaces = type.GetInterfaces()
//             .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IBackwardCompatibleContract<>));

//         foreach (var backwardCompatibleInterface in backwardCompatibleInterfaces)
//         {
//             var nextType = backwardCompatibleInterface.GetGenericArguments()[0];

//             _migrationMap[(type, nextType)] = source =>
//             {
//                 var migrateToMethod = type.GetMethod("MigrateTo");
//                 return migrateToMethod?.Invoke(source, null) ?? throw new InvalidOperationException($"MigrateTo method not found on {type.Name}");
//             };
//         }
//     }
// }