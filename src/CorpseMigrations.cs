namespace DeathCorpses
{
    internal static class CorpseMigrations
    {
        public static void RegisterAll()
        {
            // v0 -> v1: no-op. Existing fields are unchanged; the version tag is added on next save.

            // Future migration example:
            // DataMigrationRegistry.Register("corpse", 1, tree =>
            // {
            //     // Rename graveX/Y/Z to posX/Y/Z
            //     tree.SetInt("posX", tree.GetInt("graveX"));
            //     tree.SetInt("posY", tree.GetInt("graveY"));
            //     tree.SetInt("posZ", tree.GetInt("graveZ"));
            //     tree.RemoveAttribute("graveX");
            //     tree.RemoveAttribute("graveY");
            //     tree.RemoveAttribute("graveZ");
            //     return tree;
            // });
        }
    }
}
