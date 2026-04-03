using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace DeathCorpses.Lib.Config
{
    internal class ConfigMigrator
    {
        private readonly SortedDictionary<int, System.Func<Dictionary<string, JToken>, Dictionary<string, JToken>>> _migrations = new();

        public void Register(int fromVersion, System.Func<Dictionary<string, JToken>, Dictionary<string, JToken>> migration)
        {
            if (!_migrations.TryAdd(fromVersion, migration))
            {
                throw new System.ArgumentException($"Migration from version {fromVersion} is already registered");
            }
        }

        public Dictionary<string, JToken> Migrate(
            Dictionary<string, JToken> json, int loadedVersion, int targetVersion, ILogger logger)
        {
            for (int v = loadedVersion; v < targetVersion; v++)
            {
                if (_migrations.TryGetValue(v, out var migration))
                {
                    json = migration(json);
                    logger.Notification($"Config migrated from version {v} to {v + 1}");
                }
            }
            return json;
        }
    }
}
