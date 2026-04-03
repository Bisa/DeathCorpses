using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace DeathCorpses.Lib.Config
{
    internal static class ConfigMigrationRegistry
    {
        private static readonly Dictionary<System.Type, ConfigMigrator> _migrators = new();

        public static void Register<TConfig>(int fromVersion,
            System.Func<Dictionary<string, JToken>, Dictionary<string, JToken>> migration)
        {
            if (!_migrators.TryGetValue(typeof(TConfig), out var migrator))
            {
                migrator = new ConfigMigrator();
                _migrators[typeof(TConfig)] = migrator;
            }
            migrator.Register(fromVersion, migration);
        }

        public static ConfigMigrator? GetMigrator(System.Type configType)
        {
            _migrators.TryGetValue(configType, out var migrator);
            return migrator;
        }
    }
}
