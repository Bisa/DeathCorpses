using DeathCorpses.Lib.Config;
using DeathCorpses.Entities;
using DeathCorpses.Items;
using Vintagestory.API.Common;

namespace DeathCorpses
{
    internal class Core : ModSystem
    {
        public static Config Config { get; private set; } = null!;

        public override void Start(ICoreAPI api)
        {
            var configs = ModSystemRegistry.Get<ConfigManager>();
            Config = configs.GetConfig<Config>();

            api.World.Config.SetBool($"{Mod.Info.ModID}:CorpseCompassEnabled", Config.CorpseCompassEnabled);
            api.World.Config.SetBool($"{Mod.Info.ModID}:ObituariesEnabled", Config.ObituariesEnabled);

            api.RegisterEntity("EntityPlayerCorpse", typeof(EntityPlayerCorpse));
            api.RegisterItemClass("ItemCorpseCompass", typeof(ItemCorpseCompass));
            api.RegisterItemClass("ItemObituaries", typeof(ItemObituaries));
            api.RegisterItemClass("ItemObituariesBound", typeof(ItemObituariesBound));
            api.RegisterItemClass("ItemObituariesFaded", typeof(ItemObituariesFaded));
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
