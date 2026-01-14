using CommonLib.Extensions;
using CommonLib.Utils;
using HarmonyLib;
using PlayerGrave.Entities;
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

namespace PlayerGrave.Systems
{
    public class DeathContentManager : ModSystem
    {
        //private static readonly MethodInfo _resendWaypointsMethod = AccessTools.Method(typeof(WaypointMapLayer), "ResendWaypoints");
        //private static readonly MethodInfo _rebuildMapComponentsMethod = AccessTools.Method(typeof(WaypointMapLayer), "RebuildMapComponents");

        private ICoreServerAPI _sapi = null!;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            _sapi = api;
            api.Event.OnEntityDeath += OnEntityDeath;
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

            var graveEntity = CreateGraveEntity(byPlayer);
            if (graveEntity.Inventory != null && !graveEntity.Inventory.Empty)
            {
                if (Core.Config.CreateWaypoint == Config.CreateWaypointMode.Always)
                {
                    CreateDeathPoint(byPlayer.Entity, graveEntity);
                }

                // Save content for /returnthings
                if (Core.Config.MaxDeathContentSavedPerPlayer > 0)
                {
                    SaveDeathContent(graveEntity.Inventory, byPlayer);
                }

                // Spawn grave
                if (Core.Config.CreateGrave)
                {
                    _sapi.World.SpawnEntity(graveEntity);

                    string message = string.Format(
                        "Created {0} at {1}, id {2}",
                        graveEntity.GetName(),
                        graveEntity.SidedPos.XYZ.RelativePos(_sapi),
                        graveEntity.EntityId);

                    Mod.Logger.Notification(message);
                    if (Core.Config.DebugMode)
                    {
                        _sapi.BroadcastMessage(message);
                    }
                }

                // Or drop all if grave creations is disabled
                else
                {
                    graveEntity.Inventory.DropAll(graveEntity.Pos.XYZ);
                }
            }
            else
            {
                string message = $"Inventory is empty, {graveEntity.OwnerName}'s grave not created";
                Mod.Logger.Notification(message);
                if (Core.Config.DebugMode)
                {
                    _sapi.BroadcastMessage(message);
                }
            }
        }

        private EntityPlayerGrave CreateGraveEntity(IServerPlayer byPlayer)
        {
            var entityType = _sapi.World.GetEntityType(new AssetLocation(Constants.ModId, "playergrave"));

            if (_sapi.World.ClassRegistry.CreateEntity(entityType) is not EntityPlayerGrave grave)
            {
                throw new Exception("Unable to instantiate player grave");
            }

            grave.OwnerUID = byPlayer.PlayerUID;
            grave.OwnerName = byPlayer.PlayerName;
            grave.CreationTime = _sapi.World.Calendar.TotalHours;
            grave.CreationRealDatetime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            grave.Inventory = TakeContentFromPlayer(byPlayer);

            // Fix dancing grave issue
            BlockPos floorPos = TryFindFloor(byPlayer.Entity.ServerPos.AsBlockPos);

            // Attempt to align the grave to the center of the block so that it does not crawl higher
            Vec3d pos = floorPos.ToVec3d().Add(.5, 0, .5);

            grave.ServerPos.SetPos(pos);
            grave.Pos.SetPos(pos);
            grave.World = _sapi.World;

            return grave;
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
            var inv = new InventoryGeneric(GetMaxGraveSlots(byPlayer), $"playergrave-{byPlayer.PlayerUID}", _sapi);

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

        private static int GetMaxGraveSlots(IServerPlayer byPlayer)
        {
            int maxGraveSlots = 0;
            foreach (var invClassName in Core.Config.SaveInventoryTypes)
            {
                maxGraveSlots += byPlayer.InventoryManager.GetOwnInventory(invClassName)?.Count ?? 0;
            }
            return maxGraveSlots;
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

        //public static void CreateDeathPoint(EntityPlayer byPlayer, EntityPlayerGrave graveEntity)
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
        //            Guid = graveEntity.GraveId.ToString()
        //        };

        //        mapLayer.AddWaypoint(wp, byPlayer.Player as IServerPlayer);
        //    }
        //}

        public static void CreateDeathPoint(EntityPlayer byPlayer, EntityPlayerGrave graveEntity)
        {
            if (byPlayer?.Api is not ICoreServerAPI sapi) return;

            var sp = byPlayer.Player as IServerPlayer;
            if (sp == null) return;

            int absX = (int)byPlayer.ServerPos.X;
            int absY = (int)byPlayer.ServerPos.Y;
            int absZ = (int)byPlayer.ServerPos.Z;

            int halfX = sapi.World.BlockAccessor.MapSizeX / 2;
            int halfZ = sapi.World.BlockAccessor.MapSizeZ / 2;

            int x = absX - halfX;
            int y = absY;
            int z = absZ - halfZ;

            sapi.Logger.Notification($"[{Constants.ModId}] Waypoint coords (abs): {absX},{absY},{absZ}  (rel): {x},{y},{z}");

            string pinned = Core.Config.PinWaypoint ? "true" : "false";
            string color = Core.Config.WaypointColor;   // can be hex or color name
            string icon = Core.Config.WaypointIcon;

            // Quote the title so spaces are safe
            string title = Lang.Get($"{Constants.ModId}:death-waypoint-name", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            //sapi.Logger.Notification($"[PlayerGrave] Waypoint coords: {x}, {y}, {z}");
            sapi.HandleCommand(sp, $"/waypoint addati {icon} {x} {y} {z} {pinned} {color} \"{title}\"");
        }

        //private static WaypointMapLayer? GetMapLayer(ICoreAPI api)
        //{
        //    return api.ModLoader.GetModSystem<WorldMapManager>().MapLayers.FirstOrDefault(ml => ml is WaypointMapLayer) as WaypointMapLayer;
        //}

        //public static void RemoveDeathPoint(EntityPlayer byPlayer, EntityPlayerGrave graveEntity)
        //{
        //    if (byPlayer is null || graveEntity is null) return;

        //    if (byPlayer.Api is ICoreServerAPI sapi)
        //    {
        //        var serverPlayer = byPlayer.Player as IServerPlayer;
        //        var mapLayer = GetMapLayer(sapi);
        //        var waypoints = mapLayer?.Waypoints ?? [];

        //        //For every waypoint the player owns, check if it matches the grave entity id and remove it
        //        foreach (Waypoint waypoint in waypoints.ToList().Where(w => w.OwningPlayerUid == byPlayer.PlayerUID))
        //        {
        //            if (waypoint.Guid == graveEntity.GraveId.ToString())
        //            {
        //                waypoints.Remove(waypoint);
        //                _resendWaypointsMethod.Invoke(mapLayer, [serverPlayer]);
        //                _rebuildMapComponentsMethod.Invoke(mapLayer, null);
        //            }
        //        }
        //    }
        //}

        public static void RemoveDeathPoint(EntityPlayer byPlayer, EntityPlayerGrave graveEntity)
        {
            // TODO (VS 1.21): implement waypoint removal without WaypointMapLayer internals
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

        public void SaveDeathContent(InventoryGeneric inventory, IPlayer player)
        {
            string path = GetDeathDataPath(player);
            string[] files = GetDeathDataFiles(player);

            for (int i = files.Length - 1; i > Core.Config.MaxDeathContentSavedPerPlayer - 2; i--)
            {
                File.Delete(files[i]);
            }

            var tree = new TreeAttribute();
            inventory.ToTreeAttributes(tree);

            string name = $"inventory-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.dat";
            File.WriteAllBytes($"{path}/{name}", tree.ToBytes());
        }

        public InventoryGeneric LoadLastDeathContent(IPlayer player, int offset = 0)
        {
            if (Core.Config.MaxDeathContentSavedPerPlayer <= offset)
            {
                throw new IndexOutOfRangeException("offset is too large or save data disabled");
            }

            string file = GetDeathDataFiles(player).ElementAt(offset);

            var tree = new TreeAttribute();
            tree.FromBytes(File.ReadAllBytes(file));

            var inv = new InventoryGeneric(tree.GetInt("qslots"), $"playergrave-{player.PlayerUID}", player.Entity.Api);
            inv.FromTreeAttributes(tree);
            return inv;
        }
    }
}
