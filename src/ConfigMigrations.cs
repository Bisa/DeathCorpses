namespace DeathCorpses
{
    internal static class ConfigMigrations
    {
        public static void RegisterAll()
        {
            // v-1 -> v1: no-op. Unversioned files get all properties filled with defaults
            // by ConfigUtil.LoadConfig automatically. No transform needed.

            // Future migration example:
            // ConfigMigrationRegistry.Register<Config>(1, json =>
            // {
            //     // Rename a property: v1 "OldName" -> v2 "NewName"
            //     if (json.TryGetValue("OldName", out var val))
            //     {
            //         json["NewName"] = val;
            //         json.Remove("OldName");
            //     }
            //     return json;
            // });
        }
    }
}
