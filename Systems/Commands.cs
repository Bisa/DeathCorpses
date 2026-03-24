using DeathCorpses;
using DeathCorpses.Entities;
using DeathCorpses.Lib.Config;
using DeathCorpses.Lib.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace DeathCorpses.Systems
{
    internal class Commands : ModSystem
    {
        private ICoreServerAPI _sapi = null!;
        private DeathContentManager _deathContentManager = null!;
        private ConfigManager _configManager = null!;

        public override void StartServerSide(ICoreServerAPI api)
        {
            _sapi = api;
            _deathContentManager = ModSystemRegistry.Get<DeathContentManager>();
            _configManager = ModSystemRegistry.Get<ConfigManager>();

            var parsers = api.ChatCommands.Parsers;
            api.ChatCommands
                .Create("dc")
                .RequiresPrivilege(Core.Config.CommandPrivilege)
                .WithDescription("DeathCorpses admin commands")
                .BeginSubCommand("corpse")
                    .WithDescription("Manage saved corpses")
                    .BeginSubCommand("list")
                        .WithArgs(parsers.Player("player", api))
                        .HandleWith(ShowDeathList)
                    .EndSubCommand()
                    .BeginSubCommand("get")
                        .WithArgs(
                            parsers.Player("player", api),
                            parsers.Player("give to player", api),
                            parsers.OptionalInt("id", 0))
                        .HandleWith(ReturnThings)
                    .EndSubCommand()
                    .BeginSubCommand("remove")
                        .WithArgs(
                            parsers.Player("player", api),
                            parsers.OptionalInt("id", -1))
                        .HandleWith(RemoveDeathContent)
                    .EndSubCommand()
                    .BeginSubCommand("tp")
                        .WithArgs(
                            parsers.Player("player", api),
                            parsers.OptionalInt("id", 0))
                        .HandleWith(TeleportToCorpse)
                    .EndSubCommand()
                    .BeginSubCommand("tpother")
                        .WithArgs(
                            parsers.Player("target player", api),
                            parsers.Player("corpse owner", api),
                            parsers.OptionalInt("id", 0))
                        .HandleWith(TeleportOtherToCorpse)
                    .EndSubCommand()
                .EndSubCommand()
                .BeginSubCommand("config")
                    .WithDescription("View or change config settings")
                    .BeginSubCommand("list")
                        .HandleWith(ConfigList)
                    .EndSubCommand()
                    .BeginSubCommand("get")
                        .WithArgs(parsers.Word("option"))
                        .HandleWith(ConfigGet)
                    .EndSubCommand()
                    .BeginSubCommand("set")
                        .WithArgs(parsers.Word("option"), parsers.All("value"))
                        .HandleWith(ConfigSet)
                    .EndSubCommand()
                .EndSubCommand();
        }

        private TextCommandResult ShowDeathList(TextCommandCallingArgs args)
        {
            IPlayer player = (IPlayer)args[0];
            string[] files = _deathContentManager.GetDeathDataFiles(player);

            if (files.Length == 0)
            {
                return TextCommandResult.Error(Lang.Get("No saved corpses found"));
            }

            var sb = new StringBuilder();
            for (int i = 0; i < files.Length; i++)
            {
                sb.AppendLine($"{i}. {Path.GetFileName(files[i])}");
            }
            return TextCommandResult.Success(sb.ToString());
        }

        private void DespawnCorpseEntity(EntityPlayerCorpse corpse)
        {
            // Clear inventory so Die() doesn't drop items
            if (corpse.Inventory != null)
            {
                foreach (var slot in corpse.Inventory)
                {
                    slot.Itemstack = null;
                    slot.MarkDirty();
                }
            }
            corpse.Die();
        }

        private void DespawnCorpsesInLoadedEntities(string playerUid)
        {
            foreach (var entity in _sapi.World.LoadedEntities.Values.ToList())
            {
                if (entity is EntityPlayerCorpse corpse && corpse.OwnerUID == playerUid)
                {
                    DespawnCorpseEntity(corpse);
                }
            }
        }

        private void LoadChunkAndDespawnCorpse(string playerUid, BlockPos pos)
        {
            int chunkX = pos.X / _sapi.World.BlockAccessor.ChunkSize;
            int chunkZ = pos.Z / _sapi.World.BlockAccessor.ChunkSize;

            _sapi.WorldManager.LoadChunkColumnPriority(chunkX, chunkZ, new ChunkLoadOptions
            {
                OnLoaded = () => DespawnCorpsesInLoadedEntities(playerUid)
            });
        }

        private TextCommandResult RemoveDeathContent(TextCommandCallingArgs args)
        {
            IPlayer player = (IPlayer)args[0];
            int id = (int)args[1];
            string[] files = _deathContentManager.GetDeathDataFiles(player);

            if (files.Length == 0)
            {
                return TextCommandResult.Error(Lang.Get("No saved corpses found"));
            }

            if (id >= 0)
            {
                if (id >= files.Length)
                {
                    return TextCommandResult.Error(Lang.Get("Index {0} not found", id));
                }

                BlockPos? pos = _deathContentManager.LoadCorpsePosition(files[id]);
                if (pos != null)
                {
                    LoadChunkAndDespawnCorpse(player.PlayerUID, pos);
                }

                File.Delete(files[id]);
                return TextCommandResult.Success(Lang.Get(
                    "Removed corpse {0} for {1}",
                    id, player.PlayerName));
            }

            // Collect unique chunk positions from all files
            var chunkPositions = new HashSet<(int, int)>();
            foreach (string file in files)
            {
                BlockPos? pos = _deathContentManager.LoadCorpsePosition(file);
                if (pos != null)
                {
                    int chunkX = pos.X / _sapi.World.BlockAccessor.ChunkSize;
                    int chunkZ = pos.Z / _sapi.World.BlockAccessor.ChunkSize;
                    chunkPositions.Add((chunkX, chunkZ));
                }
            }

            // First despawn any already-loaded corpses
            DespawnCorpsesInLoadedEntities(player.PlayerUID);

            // Load unloaded chunks and despawn corpses in them
            foreach (var (chunkX, chunkZ) in chunkPositions)
            {
                _sapi.WorldManager.LoadChunkColumnPriority(chunkX, chunkZ, new ChunkLoadOptions
                {
                    OnLoaded = () => DespawnCorpsesInLoadedEntities(player.PlayerUID)
                });
            }

            int fileCount = files.Length;
            foreach (string file in files)
            {
                File.Delete(file);
            }

            return TextCommandResult.Success(Lang.Get(
                "Removed {0} corpse(s) for {1}",
                fileCount, player.PlayerName));
        }

        private TextCommandResult TeleportPlayerToCorpse(IServerPlayer targetPlayer, IPlayer corpseOwner, int id)
        {
            string[] files = _deathContentManager.GetDeathDataFiles(corpseOwner);

            if (files.Length == 0)
            {
                return TextCommandResult.Error(Lang.Get("No saved corpses found"));
            }

            if (id < 0 || id >= files.Length)
            {
                return TextCommandResult.Error(Lang.Get("Index {0} not found", id));
            }

            BlockPos? pos = _deathContentManager.LoadCorpsePosition(files[id]);
            if (pos == null)
            {
                return TextCommandResult.Error(Lang.Get("Corpse {0} has no saved position", id));
            }

            if (!_sapi.World.AllOnlinePlayers.Contains(targetPlayer) || targetPlayer.Entity == null)
            {
                return TextCommandResult.Error(Lang.Get(
                    "Player {0} is offline or not fully loaded.",
                    targetPlayer.PlayerName));
            }

            targetPlayer.Entity.TeleportTo(pos.ToVec3d().Add(0.5, 0, 0.5));

            return TextCommandResult.Success(Lang.Get(
                "Teleported {0} to corpse {1} of {2} at {3}, {4}, {5}",
                targetPlayer.PlayerName, id, corpseOwner.PlayerName, pos.X, pos.Y, pos.Z));
        }

        private TextCommandResult TeleportToCorpse(TextCommandCallingArgs args)
        {
            IPlayer corpseOwner = (IPlayer)args[0];
            int id = (int)args[1];

            var caller = args.Caller.Player as IServerPlayer;
            if (caller == null)
            {
                return TextCommandResult.Error(Lang.Get("You must be in-game to teleport"));
            }

            return TeleportPlayerToCorpse(caller, corpseOwner, id);
        }

        private TextCommandResult TeleportOtherToCorpse(TextCommandCallingArgs args)
        {
            IServerPlayer targetPlayer = (IServerPlayer)args[0];
            IPlayer corpseOwner = (IPlayer)args[1];
            int id = (int)args[2];

            return TeleportPlayerToCorpse(targetPlayer, corpseOwner, id);
        }

        private TextCommandResult ReturnThings(TextCommandCallingArgs args)
        {
            IPlayer player = (IPlayer)args[0];
            IPlayer giveToPlayer = (IPlayer)args[1];
            int id = (int)args[2];
            string[] files = _deathContentManager.GetDeathDataFiles(player);

            if (!_sapi.World.AllOnlinePlayers.Contains(giveToPlayer) || giveToPlayer.Entity == null)
            {
                return TextCommandResult.Error(Lang.Get(
                    "Player {0} is offline or not fully loaded.",
                    giveToPlayer.PlayerName));
            }

            if (id < 0 || files.Length <= id)
            {
                return TextCommandResult.Error(Lang.Get("Index {0} not found", id));
            }

            var dcm = ModSystemRegistry.Get<DeathContentManager>();
            InventoryGeneric inventory = dcm.LoadLastDeathContent(player, id);
            foreach (var slot in inventory)
            {
                if (slot.Empty)
                {
                    continue;
                }

                if (!giveToPlayer.InventoryManager.TryGiveItemstack(slot.Itemstack))
                {
                    _sapi.World.SpawnItemEntity(slot.Itemstack, giveToPlayer.Entity.ServerPos.XYZ.AddCopy(0, 1, 0));
                }
                slot.Itemstack = null;
                slot.MarkDirty();
            }

            return TextCommandResult.Success(Lang.Get(
                "Restored corpse inventory from {0} to {1} (index {2})",
                player.PlayerName, giveToPlayer.PlayerName, id));
        }

        private static PropertyInfo? FindConfigProperty(string name)
        {
            return ConfigUtil.GetConfigProperties(typeof(Config))
                .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private static string FormatConfigValue(object? value)
        {
            if (value is Array arr)
                return string.Join(", ", arr.Cast<object>());
            return value?.ToString() ?? "null";
        }

        private TextCommandResult ConfigList(TextCommandCallingArgs args)
        {
            var sb = new StringBuilder();
            foreach (var prop in ConfigUtil.GetConfigProperties(typeof(Config)))
            {
                sb.AppendLine($"{prop.Name} = {FormatConfigValue(prop.GetValue(Core.Config))}");
            }
            return TextCommandResult.Success(sb.ToString());
        }

        private TextCommandResult ConfigGet(TextCommandCallingArgs args)
        {
            string option = (string)args[0];

            var property = FindConfigProperty(option);
            if (property == null)
            {
                return TextCommandResult.Error(Lang.Get("Unknown config option: {0}", option));
            }

            return TextCommandResult.Success($"{property.Name} = {FormatConfigValue(property.GetValue(Core.Config))}");
        }

        private TextCommandResult ConfigSet(TextCommandCallingArgs args)
        {
            string option = (string)args[0];
            string value = ((string)args[1]).Trim();

            var property = FindConfigProperty(option);
            if (property == null)
            {
                return TextCommandResult.Error(Lang.Get("Unknown config option: {0}", option));
            }

            try
            {
                object converted;
                if (property.PropertyType.IsArray)
                {
                    var elementType = property.PropertyType.GetElementType()!;
                    var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    var arr = Array.CreateInstance(elementType, parts.Length);
                    for (int i = 0; i < parts.Length; i++)
                    {
                        arr.SetValue(Convert.ChangeType(parts[i], elementType), i);
                    }
                    converted = arr;
                }
                else
                {
                    converted = ConfigUtil.ConvertType(value, property.PropertyType, null);
                }

                property.SetValue(Core.Config, converted);
                _configManager.MarkConfigDirty(typeof(Config));

                return TextCommandResult.Success(Lang.Get(
                    "Set {0} = {1}",
                    property.Name, FormatConfigValue(converted)));
            }
            catch (Exception)
            {
                string expected = property.PropertyType.IsEnum
                    ? string.Join(", ", Enum.GetNames(property.PropertyType))
                    : property.PropertyType.Name;
                return TextCommandResult.Error(Lang.Get(
                    "Invalid value for {0}. Expected: {1}",
                    property.Name, expected));
            }
        }
    }
}
