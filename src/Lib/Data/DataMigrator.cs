using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace DeathCorpses.Lib.Data
{
    internal class DataMigrator
    {
        private readonly SortedDictionary<int, System.Func<TreeAttribute, TreeAttribute>> _migrations = new();

        public void Register(int fromVersion, System.Func<TreeAttribute, TreeAttribute> migration)
        {
            if (!_migrations.TryAdd(fromVersion, migration))
            {
                throw new System.ArgumentException($"Data migration from version {fromVersion} is already registered");
            }
        }

        public TreeAttribute Migrate(TreeAttribute tree, int loadedVersion, int targetVersion, ILogger logger)
        {
            for (int v = loadedVersion; v < targetVersion; v++)
            {
                if (_migrations.TryGetValue(v, out var migration))
                {
                    tree = migration(tree);
                    logger.Notification($"Data migrated from version {v} to {v + 1}");
                }
            }
            return tree;
        }
    }
}
