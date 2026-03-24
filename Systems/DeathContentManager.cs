using DeathCorpses.Lib.Extensions;
using DeathCorpses.Lib.Utils;
using HarmonyLib;
using DeathCorpses.Entities;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace DeathCorpses.Systems
{
    internal class DeathContentManager : ModSystem
    {
        //private static readonly MethodInfo _resendWaypointsMethod = AccessTools.Method(typeof(WaypointMapLayer), "ResendWaypoints");
        //private static readonly MethodInfo _rebuildMapComponentsMethod = AccessTools.Method(typeof(WaypointMapLayer), "RebuildMapComponents");

        private ICoreServerAPI _sapi = null!;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            _sapi = api;
            api.Event.OnEntityDeath += OnEntityDeath;
            api.Event.PlayerJoin += OnPlayerJoin;
        }

        private void OnPlayerJoin(IServerPlayer byPlayer)
        {
            
            if (!Core.Config.DisableVanillaDeathWaypoint)
                return;
            var callArgs = new TextCommandCallingArgs();
            callArgs.Caller = new Caller
            {
                Player = byPlayer,
                Entity = byPlayer.Entity
            };
            callArgs.RawArgs = new CmdArgs("deathwp false");

            _sapi.ChatCommands.Execute("waypoint", callArgs);

        }

        private void OnEntityDeath(Entity entity, DamageSource damageSource)
        {
            if (entity is EntityPlayer entityPlayer)
            {
                OnPlayerDeath((IServerPlayer)entityPlayer.Player);
            }
        }

        private void OnPlayerDeath(IServerPlayer byPlayer)
        {
            bool isKeepContent = byPlayer.Entity?.Properties?.Server?.Attributes?.GetBool("keepContents") ?? false;
            if (isKeepContent)
            {
                return;
            }

            var corpseEntity = CreateCorpseEntity(byPlayer);
            if (corpseEntity.Inventory != null && !corpseEntity.Inventory.Empty)
            {
                if (Core.Config.CreateWaypoint == Config.CreateWaypointMode.Always)
                {
                    if (byPlayer.Entity is EntityPlayer ep)
                    {
                        CreateDeathPoint(byPlayer.Entity, corpseEntity);
                    }
                }

                // Save content for /dc corpse
                if (Core.Config.MaxCorpsesSavedPerPlayer > 0)
                {                    
                    SaveDeathContent(corpseEntity.Inventory, byPlayer, corpseEntity.ServerPos.XYZ, corpseEntity.CorpseId);
                }

                // Spawn corpse
                if (Core.Config.CreateCorpse)
                {
                    _sapi.World.SpawnEntity(corpseEntity);

                    string message = string.Format(
                        "Created {0} at {1}, id {2}",
                        corpseEntity.GetName(),
                        corpseEntity.SidedPos.XYZ.RelativePos(_sapi),
                        corpseEntity.EntityId);

                    Mod.Logger.Notification(message);
                    if (Core.Config.DebugMode)
                    {
                        _sapi.BroadcastMessage(message);
                    }
                }

                // Or drop all if corpse creation is disabled
                else
                {
                    corpseEntity.Inventory.DropAll(corpseEntity.Pos.XYZ);
                }
            }
            else
            {
                string message = $"Inventory is empty, {corpseEntity.OwnerName}'s corpse not created";
                Mod.Logger.Notification(message);
                if (Core.Config.DebugMode)
                {
                    _sapi.BroadcastMessage(message);
                }
            }
        }

        private EntityPlayerCorpse CreateCorpseEntity(IServerPlayer byPlayer)
        {
            var entityType = _sapi.World.GetEntityType(new AssetLocation(Constants.ModId, "deathcorpses"));

            if (_sapi.World.ClassRegistry.CreateEntity(entityType) is not EntityPlayerCorpse corpse)
            {
                throw new Exception("Unable to instantiate player corpse");
            }

            corpse.OwnerUID = byPlayer.PlayerUID;
            corpse.OwnerName = byPlayer.PlayerName;
            corpse.CreationTime = _sapi.World.Calendar.TotalHours;
            corpse.CreationRealDatetime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            corpse.Inventory = TakeContentFromPlayer(byPlayer);

            // Fix dancing corpse issue
            BlockPos floorPos = TryFindFloor(byPlayer.Entity.ServerPos.AsBlockPos);

            // Attempt to align the corpse to the center of the block so that it does not crawl higher
            Vec3d pos = floorPos.ToVec3d().Add(.5, 0, .5);

            corpse.ServerPos.SetPos(pos);
            corpse.Pos.SetPos(pos);
            corpse.World = _sapi.World;

            return corpse;
        }

        /// <summary> Try to find the nearest block with collision below </summary>
        private BlockPos TryFindFloor(BlockPos pos)
        {
            var floorPos = new BlockPos(pos.dimension);
            for (int i = pos.Y; i > 0; i--)
            {
                floorPos.Set(pos.X, i, pos.Z);
                var block = _sapi.World.BlockAccessor.GetBlock(floorPos);
                if (block.BlockId != 0 && block.CollisionBoxes?.Length > 0)
                {
                    floorPos.Set(pos.X, i + 1, pos.Z);
                    return floorPos;
                }
            }
            return pos;
        }

        private InventoryGeneric TakeContentFromPlayer(IServerPlayer byPlayer)
        {
            var inv = new InventoryGeneric(GetMaxCorpseSlots(byPlayer), $"deathcorpses-{byPlayer.PlayerUID}", _sapi);

            int lastSlotId = 0;
            foreach (var invClassName in Core.Config.SaveInventoryTypes)
            {
                // Skip armor if it does not drop after death
                var isDropArmorVanilla = byPlayer.Entity.Properties.Server?.Attributes?.GetBool("dropArmorOnDeath") ?? false;
                var isDropArmor = isDropArmorVanilla || Core.Config.DropArmorOnDeath != Config.DropArmorMode.Vanilla;
                if (invClassName == GlobalConstants.characterInvClassName && !isDropArmor)
                {
                    continue;
                }

                // XSkills slots fix
                if (invClassName.Equals(GlobalConstants.backpackInvClassName) &&
                    byPlayer.InventoryManager.GetOwnInventory("xskillshotbar") != null)
                {
                    int i = 0;
                    var backpackInv = byPlayer.InventoryManager.GetOwnInventory(invClassName);
                    foreach (var slot in backpackInv)
                    {
                        if (i > backpackInv.Count - 4) // Extra backpack slots
                        {
                            break;
                        }
                        inv[lastSlotId++].Itemstack = TakeSlotContent(slot);
                    }
                    continue;
                }

                foreach (var slot in byPlayer.InventoryManager.GetOwnInventory(invClassName))
                {
                    inv[lastSlotId++].Itemstack = TakeSlotContent(slot);
                }
            }

            return inv;
        }

        private static int GetMaxCorpseSlots(IServerPlayer byPlayer)
        {
            int maxCorpseSlots = 0;
            foreach (var invClassName in Core.Config.SaveInventoryTypes)
            {
                maxCorpseSlots += byPlayer.InventoryManager.GetOwnInventory(invClassName)?.Count ?? 0;
            }
            return maxCorpseSlots;
        }

        private static ItemStack? TakeSlotContent(ItemSlot slot)
        {
            if (slot.Empty)
            {
                return null;
            }

            // Skip the player's clothing (not armor)
            if (slot.Inventory.ClassName == GlobalConstants.characterInvClassName)
            {
                bool isArmor = slot.Itemstack.ItemAttributes?["protectionModifiers"].Exists ?? false;
                if (!isArmor && Core.Config.DropArmorOnDeath != Config.DropArmorMode.ArmorAndCloth)
                {
                    return null;
                }
            }

            return slot.TakeOutWhole();
        }

        //public static void CreateDeathPoint(EntityPlayer byPlayer, EntityPlayerCorpse corpseEntity)
        //{
        //    if (byPlayer.Api is ICoreServerAPI)
        //    {
        //        var mapLayer = GetMapLayer(byPlayer.Api);

        //        if (mapLayer is null)
        //        {
        //            byPlayer.Api.Logger.Error("Failed to create waypoint, maplayer is null");
        //            return;
        //        }

        //        Waypoint wp = new()
        //        {
        //            Position = byPlayer.ServerPos.AsBlockPos.ToVec3d(),
        //            Title = Lang.Get($"{Constants.ModId}:death-waypoint-name", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
        //            Pinned = Core.Config.PinWaypoint,
        //            Icon = Core.Config.WaypointIcon,
        //            Color = ColorTranslator.FromHtml(Core.Config.WaypointColor).ToArgb(),
        //            OwningPlayerUid = byPlayer.PlayerUID,
        //            Guid = corpseEntity.CorpseId.ToString()
        //        };

        //        mapLayer.AddWaypoint(wp, byPlayer.Player as IServerPlayer);
        //    }
        //}

        public static void CreateDeathPoint(EntityPlayer byPlayer, EntityPlayerCorpse corpseEntity)
        {
            if (byPlayer?.Api is not ICoreServerAPI sapi) return;

            var sp = byPlayer.Player as IServerPlayer;
            if (sp == null) return;

            int absX = (int)corpseEntity.ServerPos.X;
            int absY = (int)corpseEntity.ServerPos.Y;
            int absZ = (int)corpseEntity.ServerPos.Z;

            var spawn = sapi.World.DefaultSpawnPosition;
            int x = absX - (int)spawn.X;
            int y = absY;
            int z = absZ - (int)spawn.Z;

            sapi.Logger.Notification($"[{Constants.ModId}] Waypoint coords (abs): {absX},{absY},{absZ}  (rel): {x},{y},{z}");

            string pinned = Core.Config.PinWaypoint ? "true" : "false";
            string color = Core.Config.WaypointColor;   // can be hex or color name
            string icon = Core.Config.WaypointIcon;

            // Quote the title so spaces are safe
            string title = Lang.Get($"{Constants.ModId}:death-waypoint-name", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            //sapi.Logger.Notification($"[PlayerGrave] Waypoint coords: {x}, {y}, {z}");
            //sapi.HandleCommand(sp, $"/waypoint addati {icon} {x} {y} {z} {pinned} {color} \"{title}\"");
            var callArgs = new TextCommandCallingArgs();
            callArgs.Caller = new Caller
            {
                Player = sp,
                Entity = byPlayer
            };
            callArgs.RawArgs = new CmdArgs($"addati {icon} {x} {y} {z} {pinned} {color} \"{title}\"");
            sapi.ChatCommands.Execute("waypoint", callArgs, (result) =>
            {
                sapi.Logger.Notification($"Waypoint cmd: {result.Status} - {result.StatusMessage}");
                if (result.Status != EnumCommandStatus.Success) return;
                var m = Regex.Match(result.StatusMessage, @"nr\.\s*(\d+)");
                if (m.Success) corpseEntity.WaypointID = int.Parse(m.Groups[1].Value);
            });
        }

        //private static WaypointMapLayer? GetMapLayer(ICoreAPI api)
        //{
        //    return api.ModLoader.GetModSystem<WorldMapManager>().MapLayers.FirstOrDefault(ml => ml is WaypointMapLayer) as WaypointMapLayer;
        //}

        //public static void RemoveDeathPoint(EntityPlayer byPlayer, EntityPlayerCorpse corpseEntity)
        //{
        //    if (byPlayer is null || corpseEntity is null) return;

        //    if (byPlayer.Api is ICoreServerAPI sapi)
        //    {
        //        var serverPlayer = byPlayer.Player as IServerPlayer;
        //        var mapLayer = GetMapLayer(sapi);
        //        var waypoints = mapLayer?.Waypoints ?? [];

        //        //For every waypoint the player owns, check if it matches the corpse entity id and remove it
        //        foreach (Waypoint waypoint in waypoints.ToList().Where(w => w.OwningPlayerUid == byPlayer.PlayerUID))
        //        {
        //            if (waypoint.Guid == corpseEntity.CorpseId.ToString())
        //            {
        //                waypoints.Remove(waypoint);
        //                _resendWaypointsMethod.Invoke(mapLayer, [serverPlayer]);
        //                _rebuildMapComponentsMethod.Invoke(mapLayer, null);
        //            }
        //        }
        //    }
        //}

        public static void RemoveDeathPoint(EntityPlayer byPlayer, EntityPlayerCorpse corpseEntity)
        {
            if (corpseEntity.WaypointID < 0) return;
            if (byPlayer?.Api is not ICoreServerAPI sapi) return;

            var sp = byPlayer.Player as IServerPlayer;
            if (sp == null) return;

            var callArgs = new TextCommandCallingArgs
            {
                Caller = new Caller
                {
                    Player = sp,
                    Entity = byPlayer
                },
                RawArgs = new CmdArgs($"remove {corpseEntity.WaypointID}")
            };

            sapi.ChatCommands.Execute("waypoint", callArgs, (result) =>
            {
                sapi.Logger.Notification(
                    $"Waypoint remove: {result.Status} - {result.StatusMessage}"
                );
            });
        }

        public string GetDeathDataPath(IPlayer player)
        {
            ICoreAPI api = player.Entity.Api;
            string uidFixed = Regex.Replace(player.PlayerUID, "[^0-9a-zA-Z]", "");
            string localPath = Path.Combine("ModData", api.GetWorldId(), Mod.Info.ModID, uidFixed);
            return api.GetOrCreateDataPath(localPath);
        }

        public string[] GetDeathDataFiles(IPlayer player)
        {
            string path = GetDeathDataPath(player);
            return Directory
                .GetFiles(path)
                .OrderByDescending(f => new FileInfo(f).Name)
                .ToArray();
        }

        public void SaveDeathContent(InventoryGeneric inventory, IPlayer player, Vec3d gravePos, string corpseId)
        {
            string path = GetDeathDataPath(player);
            string[] files = GetDeathDataFiles(player);

            for (int i = files.Length - 1; i > Core.Config.MaxCorpsesSavedPerPlayer - 2; i--)
            {
                File.Delete(files[i]);
            }

            var tree = new TreeAttribute();
            inventory.ToTreeAttributes(tree);
            tree.SetInt("graveX", (int)gravePos.X);
            tree.SetInt("graveY", (int)gravePos.Y);
            tree.SetInt("graveZ", (int)gravePos.Z);
            tree.SetString("corpseId", corpseId);

            string name = $"inventory-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.dat";
            File.WriteAllBytes($"{path}/{name}", tree.ToBytes());
        }

        public BlockPos? LoadCorpsePosition(string filePath)
        {
            var tree = new TreeAttribute();
            tree.FromBytes(File.ReadAllBytes(filePath));

            if (!tree.HasAttribute("graveX"))
                return null;

            return new BlockPos(
                tree.GetInt("graveX"),
                tree.GetInt("graveY"),
                tree.GetInt("graveZ"));
        }

        public string? LoadCorpseId(string filePath)
        {
            var tree = new TreeAttribute();
            tree.FromBytes(File.ReadAllBytes(filePath));
            return tree.GetString("corpseId");
        }

        public void UpdateCorpsePosition(string filePath, Vec3d newPos)
        {
            var tree = new TreeAttribute();
            tree.FromBytes(File.ReadAllBytes(filePath));
            tree.SetInt("graveX", (int)newPos.X);
            tree.SetInt("graveY", (int)newPos.Y);
            tree.SetInt("graveZ", (int)newPos.Z);
            File.WriteAllBytes(filePath, tree.ToBytes());
        }

        public InventoryGeneric LoadLastDeathContent(IPlayer player, int offset = 0)
        {
            if (Core.Config.MaxCorpsesSavedPerPlayer <= offset)
            {
                throw new IndexOutOfRangeException("offset is too large or save data disabled");
            }

            string file = GetDeathDataFiles(player).ElementAt(offset);

            var tree = new TreeAttribute();
            tree.FromBytes(File.ReadAllBytes(file));

            var inv = new InventoryGeneric(tree.GetInt("qslots"), $"deathcorpses-{player.PlayerUID}", player.Entity.Api);
            inv.FromTreeAttributes(tree);
            return inv;
        }
    }
}
