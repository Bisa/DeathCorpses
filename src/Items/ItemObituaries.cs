using DeathCorpses.Lib.Extensions;
using DeathCorpses.Lib.Utils;
using DeathCorpses.Entities;
using DeathCorpses.Systems;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace DeathCorpses.Items
{
    /// <summary>
    /// Unbound obituary — stackable, sold by traders.
    /// On right-click, attempts to bind to a random corpse.
    /// If successful, consumes one from the stack and gives the player a bound obituary.
    /// If no corpses exist, shows a message and keeps the item.
    /// </summary>
    public class ItemObituaries : Item
    {
        public static long SearchCooldown => 5000;

        private ILogger? _modLogger;
        public ILogger ModLogger => _modLogger ?? api.Logger;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            _modLogger = ModSystemRegistry.Get<Core>().Mod.Logger;
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);

            if (handling == EnumHandHandling.NotHandled)
            {
                long lastSearch = slot.Itemstack.TempAttributes.GetLong("lastObituarySearch", 0);
                if (lastSearch + SearchCooldown < api.World.ElapsedMilliseconds)
                {
                    TryBind(byEntity, slot);
                    slot.Itemstack?.TempAttributes.SetLong("lastObituarySearch", api.World.ElapsedMilliseconds);
                    handling = EnumHandHandling.PreventDefault;
                }
            }
        }

        private void TryBind(EntityAgent byEntity, ItemSlot slot)
        {
            if (api.Side != EnumAppSide.Server) return;

            var sapi = (ICoreServerAPI)api;
            var dcm = ModSystemRegistry.Get<DeathContentManager>();

            string? corpseId = null;
            string? ownerName = null;
            Vec3i? corpsePos = null;

            // Try online players' corpses first if configured
            if (Core.Config.ObituariesFavorOnline)
            {
                var record = dcm.GetRandomCorpseFromDisk(sapi.World.Rand, onlineOnly: true);
                if (record != null)
                {
                    corpseId = record.CorpseId;
                    ownerName = record.OwnerName;
                    corpsePos = new Vec3i(record.Position.X, record.Position.Y, record.Position.Z);
                }
            }

            // Fall back to all corpses on disk
            if (corpseId == null)
            {
                var record = dcm.GetRandomCorpseFromDisk(sapi.World.Rand);
                if (record != null)
                {
                    corpseId = record.CorpseId;
                    ownerName = record.OwnerName;
                    corpsePos = new Vec3i(record.Position.X, record.Position.Y, record.Position.Z);
                }
            }

            if (corpseId == null || ownerName == null || corpsePos == null)
            {
                byEntity.SendMessage(Lang.Get($"{Constants.ModId}:obituaries-no-corpses"));
                return;
            }

            // Create bound obituary
            var boundItem = api.World.GetItem(new AssetLocation(Constants.ModId, "obituaries-bound"));
            if (boundItem == null)
            {
                ModLogger.Error("Could not find obituaries-bound item type");
                return;
            }

            var boundStack = new ItemStack(boundItem, 1);
            int hintRadius = Core.Config.ObituariesHintRadius;
            Vec3i hintPos = RandomizeAroundPos(corpsePos, hintRadius, sapi.World.Rand);
            boundStack.Attributes.SetVec3i("obituaryCorpsePos", hintPos);
            boundStack.Attributes.SetString("obituaryOwnerName", ownerName);
            boundStack.Attributes.SetString("obituaryCorpseId", corpseId);

            // Consume one unbound obituary
            slot.Itemstack.StackSize--;
            if (slot.Itemstack.StackSize <= 0)
            {
                slot.Itemstack = null;
            }
            slot.MarkDirty();

            // Give bound obituary to player
            if (byEntity is EntityPlayer playerEntity)
            {
                if (!playerEntity.Player.InventoryManager.TryGiveItemstack(boundStack))
                {
                    api.World.SpawnItemEntity(boundStack, byEntity.Pos.XYZ);
                }
            }

            byEntity.SendMessage(Lang.Get($"{Constants.ModId}:obituaries-hint", ownerName));
            ModLogger.Notification($"Obituary bound to {ownerName}'s corpse at {corpsePos}");
        }

        // --- Shared utilities used by all obituary item types ---

        internal static EntityPlayerCorpse? FindLoadedCorpseById(ICoreServerAPI sapi, string corpseId)
        {
            foreach (Entity entity in sapi.World.LoadedEntities.Values)
            {
                if (entity is EntityPlayerCorpse corpse && corpse.CorpseId == corpseId)
                {
                    return corpse;
                }
            }
            return null;
        }

        internal static Vec3i RandomizeAroundPos(Vec3i center, int radius, Random rand)
        {
            int x, y, z;
            do
            {
                x = rand.Next(-radius, radius + 1);
                y = rand.Next(-radius, radius + 1);
                z = rand.Next(-radius, radius + 1);
            } while (x * x + y * y + z * z > radius * radius);

            return new Vec3i(center.X + x, center.Y + y, center.Z + z);
        }

        internal static int GetGhostlyColor(Random rand)
        {
            int a = 255;
            int r = rand.Next(150, 200);
            int g = rand.Next(180, 230);
            int b = rand.Next(220, 256);
            return ColorUtil.ToRgba(a, r, g, b);
        }
    }
}
