using System.Collections.Generic;
using Vintagestory.API.Datastructures;

namespace DeathCorpses.Lib.Data
{
    internal static class DataMigrationRegistry
    {
        private static readonly Dictionary<string, DataMigrator> _migrators = new();

        public static void Register(string key, int fromVersion, System.Func<TreeAttribute, TreeAttribute> migration)
        {
            if (!_migrators.TryGetValue(key, out var migrator))
            {
                migrator = new DataMigrator();
                _migrators[key] = migrator;
            }
            migrator.Register(fromVersion, migration);
        }

        public static DataMigrator? GetMigrator(string key)
        {
            _migrators.TryGetValue(key, out var migrator);
            return migrator;
        }
    }
}
