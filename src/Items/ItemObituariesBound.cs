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
    /// Bound obituary — unique (stack 1), tracks a specific corpse.
    /// Emits particles pointing toward a randomized hint position near the corpse.
    /// Each right-click refreshes the hint with a new random offset.
    /// If the corpse no longer exists, converts into a faded obituary.
    /// </summary>
    public class ItemObituariesBound : Item
    {
        public static long SearchCooldown => 5000;
        public static long OffHandParticleEmitCooldown => 250;
        public static long CorpseExistenceCheckInterval => 10000;

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
                    HandleInteract(byEntity, slot);
                    slot.Itemstack?.TempAttributes.SetLong("lastObituarySearch", api.World.ElapsedMilliseconds);
                    handling = EnumHandHandling.PreventDefault;
                }

                if (slot.Itemstack != null)
                {
                    EmitParticles(slot, byEntity);
                }
            }
        }

        public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
        {
            if (api.Side == EnumAppSide.Server)
            {
                long lastCheck = slot.Itemstack.TempAttributes.GetLong("lastCorpseExistenceCheck", 0);
                if (lastCheck + CorpseExistenceCheckInterval < byEntity.World.ElapsedMilliseconds)
                {
                    slot.Itemstack.TempAttributes.SetLong("lastCorpseExistenceCheck", byEntity.World.ElapsedMilliseconds);
                    string? corpseId = slot.Itemstack.Attributes.GetString("obituaryCorpseId");
                    if (corpseId != null && !ModSystemRegistry.Get<DeathContentManager>().CorpseExistsOnDisk(corpseId))
                    {
                        string? ownerName = slot.Itemstack.Attributes.GetString("obituaryOwnerName");
                        byEntity.SendMessage(Lang.Get($"{Constants.ModId}:obituaries-trail-cold", ownerName ?? "unknown"));
                        ConvertToFaded(slot, ownerName ?? "unknown");
                        return;
                    }

                    // Refresh hint position if corpse entity is loaded
                    if (corpseId != null && api is ICoreServerAPI sapi)
                    {
                        var corpse = ItemObituaries.FindLoadedCorpseById(sapi, corpseId);
                        if (corpse != null)
                        {
                            int hintRadius = Core.Config.ObituariesHintRadius;
                            Vec3i hintPos = ItemObituaries.RandomizeAroundPos(corpse.Pos.XYZInt, hintRadius, api.World.Rand);
                            slot.Itemstack.Attributes.SetVec3i("obituaryCorpsePos", hintPos);
                            slot.MarkDirty();
                        }
                    }
                }
            }

            if (byEntity.LeftHandItemSlot == slot)
            {
                long lastEmit = slot.Itemstack.TempAttributes.GetLong("lastEmitParticlesOffHand", 0);
                if (lastEmit + OffHandParticleEmitCooldown < byEntity.World.ElapsedMilliseconds)
                {
                    EmitParticles(slot, byEntity);
                    slot.Itemstack.TempAttributes.SetLong("lastEmitParticlesOffHand", byEntity.World.ElapsedMilliseconds);
                }
            }

            base.OnHeldIdle(slot, byEntity);
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            string? ownerName = inSlot.Itemstack.Attributes.GetString("obituaryOwnerName");
            if (ownerName != null)
            {
                dsc.AppendLine(Lang.Get($"{Constants.ModId}:obituaries-bound", ownerName));
            }
        }

        private void HandleInteract(EntityAgent byEntity, ItemSlot slot)
        {
            if (api.Side != EnumAppSide.Server) return;

            string? ownerName = slot.Itemstack.Attributes.GetString("obituaryOwnerName");
            if (ownerName == null) return;

            // Tier 1: corpse entity is loaded — update hint with fresh position
            string? corpseId = slot.Itemstack.Attributes.GetString("obituaryCorpseId");
            var corpse = corpseId != null && api is ICoreServerAPI sapi
                ? ItemObituaries.FindLoadedCorpseById(sapi, corpseId)
                : null;
            if (corpse != null)
            {
                int hintRadius = Core.Config.ObituariesHintRadius;
                Vec3i hintPos = ItemObituaries.RandomizeAroundPos(corpse.Pos.XYZInt, hintRadius, api.World.Rand);
                slot.Itemstack.Attributes.SetVec3i("obituaryCorpsePos", hintPos);
                slot.MarkDirty();

                byEntity.SendMessage(Lang.Get($"{Constants.ModId}:obituaries-hint", ownerName));

                if (Core.Config.DebugMode)
                {
                    byEntity.SendMessage($"Obituary hint: {hintPos} (actual: {corpse.SidedPos.XYZ})");
                }
                return;
            }

            // Tier 2: corpse not loaded but exists on disk — it's just in an unloaded chunk
            if (corpseId != null && ModSystemRegistry.Get<DeathContentManager>().CorpseExistsOnDisk(corpseId))
            {
                byEntity.SendMessage(Lang.Get($"{Constants.ModId}:obituaries-distant", ownerName));
                return;
            }

            // Tier 3: corpse is truly gone — fade the obituary
            byEntity.SendMessage(Lang.Get($"{Constants.ModId}:obituaries-trail-cold", ownerName));
            ConvertToFaded(slot, ownerName);
        }

        private void ConvertToFaded(ItemSlot slot, string ownerName)
        {
            var fadedItem = api.World.GetItem(new AssetLocation(Constants.ModId, "obituaries-faded"));
            if (fadedItem == null)
            {
                ModLogger.Error("Could not find obituaries-faded item type");
                slot.Itemstack = null;
                slot.MarkDirty();
                return;
            }

            var fadedStack = new ItemStack(fadedItem, 1);
            fadedStack.Attributes.SetString("obituaryOwnerName", ownerName);

            slot.Itemstack = fadedStack;
            slot.MarkDirty();
        }

        private void EmitParticles(ItemSlot slot, EntityAgent byEntity)
        {
            Vec3i targetPos = slot.Itemstack.Attributes.GetVec3i("obituaryCorpsePos");
            if (api.Side == EnumAppSide.Client && targetPos != null)
            {
                var targetVec = targetPos.ToVec3d().Add(.5, 0, .5);
                var startPos = byEntity.SidedPos.AheadCopy(1).XYZ.Add(0, byEntity.LocalEyePos.Y, 0);
                var relativePos = targetVec - startPos;

                float lifeLength = 1f;
                var velocity = relativePos.ToVec3f() / (lifeLength * 3);
                float minSize = GameMath.Clamp(velocity.Length() * 0.01f, 0.05f, 3f);

                var particles = new SimpleParticleProperties
                {
                    MinPos = startPos,
                    AddPos = velocity.ToVec3d() * 0.1,
                    MinVelocity = velocity,
                    AddVelocity = Vec3f.Zero,
                    RandomVelocityChange = true,
                    Bounciness = 0.1f,
                    GravityEffect = 0,
                    WindAffected = false,
                    WithTerrainCollision = true,
                    MinSize = minSize,
                    MaxSize = minSize * 2,
                    MinQuantity = 1,
                    AddQuantity = 5,
                    LifeLength = lifeLength,
                    VertexFlags = 100 & VertexFlags.GlowLevelBitMask,
                    ParticleModel = EnumParticleModel.Quad,
                    Color = ItemObituaries.GetGhostlyColor(api.World.Rand)
                };

                api.World.SpawnParticles(particles);
            }
        }
    }
}
