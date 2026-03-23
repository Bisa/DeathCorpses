using DeathCorpses.Lib.Config;
using DeathCorpses.Entities;
using DeathCorpses.Items;
using Vintagestory.API.Common;

namespace DeathCorpses
{
    public class Core : ModSystem
    {
        public static Config Config { get; private set; } = null!;

        public override void Start(ICoreAPI api)
        {
            var configs = api.ModLoader.GetModSystem<ConfigManager>();
            Config = configs.GetConfig<Config>();

            api.World.Config.SetBool($"{Mod.Info.ModID}:GraveCompassEnabled", Config.GraveCompassEnabled);

            api.RegisterEntity("EntityPlayerGrave", typeof(EntityPlayerGrave));
            api.RegisterItemClass("ItemGraveCompass", typeof(ItemGraveCompass));
        }

        public override void AssetsLoaded(ICoreAPI api)
        {
            if (Config.CreateWaypoint == Config.CreateWaypointMode.Auto)
            {
                Config.CreateWaypoint = Config.CreateWaypointMode.Always;

                var hasDeathWaypointsMods = api.Assets.Get<string[]>($"{Mod.Info.ModID}:config/hasdeathwaypointsmods.json");
                foreach (var modid in hasDeathWaypointsMods)
                {
                    if (api.ModLoader.IsModEnabled(modid))
                    {
                        Config.CreateWaypoint = Config.CreateWaypointMode.None;
                    }
                }
            }
        }
    }
}
