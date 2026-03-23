using DeathCorpses.Lib.Extensions;
using DeathCorpses.Lib.Utils;
using DeathCorpses.Entities;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace DeathCorpses.Items
{
    public class ItemGraveCompass : Item
    {
        public static long SearchCooldown => 5000;
        public static long OffHandSearchCooldown => 10000;
        public static long OffHandParticleEmitCooldown => 250;
        public static int SearchRadius => 3;

        private readonly SimpleParticleProperties _particles = new()
        {
            MinPos = Vec3d.Zero,
            AddPos = new Vec3d(.2, .2, .2),

            MinVelocity = Vec3f.Zero,
            AddVelocity = Vec3f.Zero,
            RandomVelocityChange = true,

            Bounciness = 0.1f,
            GravityEffect = 0,
            WindAffected = false,
            WithTerrainCollision = true,

            MinSize = 0.3f,
            MaxSize = 0.8f,
            MinQuantity = 1,
            AddQuantity = 5,
            LifeLength = 1f,

            VertexFlags = 100 & VertexFlags.GlowLevelBitMask,
            ParticleModel = EnumParticleModel.Quad
        };

        private ILogger? _modLogger;
        public ILogger ModLogger => _modLogger ?? api.Logger;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            _modLogger = api.ModLoader.GetModSystem<Core>().Mod.Logger;
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);

            if (handling == EnumHandHandling.NotHandled)
            {
                long lastGraveSearch = slot.Itemstack.TempAttributes.GetLong("lastGraveSearch", 0);
                if (lastGraveSearch + SearchCooldown < api.World.ElapsedMilliseconds)
                {
                    UpdateNearestGrave(byEntity, slot);
                    slot.Itemstack.TempAttributes.SetLong("lastGraveSearch", api.World.ElapsedMilliseconds);
                    handling = EnumHandHandling.PreventDefault;
                }

                EmitParticles(slot, byEntity);
            }
        }

        public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
        {
            if (byEntity.LeftHandItemSlot == slot)
            {
                long lastGraveSearch = slot.Itemstack.TempAttributes.GetLong("lastGraveSearch", 0);
                if (lastGraveSearch + OffHandSearchCooldown < api.World.ElapsedMilliseconds)
                {
                    UpdateNearestGrave(byEntity, slot);
                    slot.Itemstack.TempAttributes.SetLong("lastGraveSearch", api.World.ElapsedMilliseconds);
                }

                long lastEmit = slot.Itemstack.TempAttributes.GetLong("lastEmitParticlesOffHand", 0);
                if (lastEmit + OffHandParticleEmitCooldown < byEntity.World.ElapsedMilliseconds)
                {
                    EmitParticles(slot, byEntity);
                    slot.Itemstack.TempAttributes.SetLong("lastEmitParticlesOffHand", byEntity.World.ElapsedMilliseconds);
                }
            }

            base.OnHeldIdle(slot, byEntity);
        }

        private void EmitParticles(ItemSlot slot, EntityAgent byEntity)
        {
            Vec3i nearestGravePos = slot.Itemstack.Attributes.GetVec3i("nearestGravePos");
            if (api.Side == EnumAppSide.Client && nearestGravePos != null)
            {
                var targetPos = nearestGravePos.ToVec3d().Add(.5, 0, .5);
                var startPos = byEntity.SidedPos.AheadCopy(1).XYZ.Add(0, byEntity.LocalEyePos.Y, 0);
                var relativePos = targetPos - startPos;


                _particles.MinVelocity = relativePos.ToVec3f() / (_particles.LifeLength * 3);
                _particles.MinPos = startPos;
                _particles.AddPos = _particles.MinVelocity.ToVec3d() * 0.1;

                _particles.MinSize = GameMath.Clamp(_particles.MinVelocity.Length() * 0.01f, 0.05f, 3f);
                _particles.MaxSize = _particles.MinSize * 2;

                _particles.Color = GetRandomColor(api.World.Rand);
                api.World.SpawnParticles(_particles);
            }
        }

        private void UpdateNearestGrave(EntityAgent byEntity, ItemSlot slot)
        {
            if (api.Side == EnumAppSide.Server && byEntity is EntityPlayer playerEntity)
            {
                double distance = double.MaxValue;
                EntityPlayerGrave? nearestGrave = null;

                string? ownerUID = playerEntity.PlayerUID;
                if (playerEntity.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
                {
                    ownerUID = null; // show all graves in creative
                }

                foreach (EntityPlayerGrave grave in GetGravesAround(SearchRadius, byEntity.ServerPos.XYZInt, ownerUID))
                {
                    double currDistance = byEntity.Pos.SquareDistanceTo(grave.Pos);
                    if (currDistance <= distance)
                    {
                        distance = currDistance;
                        nearestGrave = grave;
                    }
                }

                if (nearestGrave != null)
                {
                    slot.Itemstack.Attributes.SetVec3i("nearestGravePos", nearestGrave.Pos.XYZInt);
                    slot.MarkDirty();

                    string text = $"{nearestGrave.OwnerName}'s grave found at {nearestGrave.SidedPos.XYZ}";
                    ModLogger.Notification(text);
                    if (Core.Config.DebugMode)
                    {
                        byEntity.SendMessage(text);
                    }
                }
                else
                {
                    slot.Itemstack.Attributes.RemoveAttribute("nearestGravePosX");
                    slot.Itemstack.Attributes.RemoveAttribute("nearestGravePosY");
                    slot.Itemstack.Attributes.RemoveAttribute("nearestGravePosZ");
                    slot.MarkDirty();

                    byEntity.SendMessage(Lang.Get($"{Constants.ModId}:gravecompass-graves-not-found"));
                }
            }
        }

        private IEnumerable<EntityPlayerGrave> GetGravesAround(int radius, Vec3i pos, string? playerUID = null)
        {
            foreach (IServerChunk chunk in GetAllChunksAround(radius, pos))
            {
                if (chunk.Entities != null)
                {
                    foreach (var entity in chunk.Entities)
                    {
                        if (entity is EntityPlayerGrave graveEntity)
                        {
                            if (playerUID == null || graveEntity.OwnerUID == playerUID)
                            {
                                yield return graveEntity;
                            }
                        }
                    }
                }
            }
        }

        private IEnumerable<IServerChunk> GetAllChunksAround(int radius, Vec3i pos)
        {
            var sapi = (ICoreServerAPI)api;

            int chunkSize = sapi.WorldManager.ChunkSize;
            int chunksInColum = sapi.WorldManager.MapSizeY / chunkSize;

            int chunkX = pos.X / chunkSize;
            int chunkZ = pos.Z / chunkSize;

            for (int i = chunkX - radius; i <= chunkX + radius; i++)
            {
                for (int j = chunkZ - radius; j <= chunkZ + radius; j++)
                {
                    for (int k = 0; k < chunksInColum; k++)
                    {
                        var chunk = sapi.WorldManager.GetChunk(i, k, j);
                        if (chunk != null)
                        {
                            yield return chunk;
                        }
                        else
                        {
                            ModLogger.Warning("Chunk at X={0} Y={1} Z={2} is not loaded", i, k, j);
                        }
                    }
                }
            }
        }

        private static int GetRandomColor(Random rand)
        {
            int a = 255;
            int r = rand.Next(200, 256);
            int g = rand.Next(100, 156);
            int b = rand.Next(0, 56);

            return ColorUtil.ToRgba(a, r, g, b);
        }
    }
}
