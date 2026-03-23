using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace DeathCorpses.Lib.Utils
{
    public static class ChatUtil
    {
        public static void SendMessage(this IServerPlayer player, string msg, int chatGroup = -1)
        {
            if (chatGroup == -1) chatGroup = GlobalConstants.CurrentChatGroup;
            player.SendMessage(chatGroup, msg, EnumChatType.Notification);
            player.Entity.Api.World.Logger.Chat(msg);
        }

        public static void SendMessage(this IClientPlayer player, string msg, int chatGroup = -1)
        {
            if (chatGroup == -1) chatGroup = GlobalConstants.CurrentChatGroup;
            var capi = (ICoreClientAPI)player.Entity.Api;
            capi.SendChatMessage(msg, chatGroup);
            capi.World.Logger.Chat(msg);
        }

        public static void SendMessage(this IPlayer player, string msg, int chatGroup = -1)
        {
            (player as IServerPlayer)?.SendMessage(msg, chatGroup);
            (player as IClientPlayer)?.SendMessage(msg, chatGroup);
        }

        public static void SendMessage(this Entity playerEntity, string msg, int chatGroup = -1)
        {
            ICoreAPI api = playerEntity.Api;
            IPlayer? player = api.World.PlayerByUid((playerEntity as EntityPlayer)?.PlayerUID);

            if (player is not null)
            {
                player.SendMessage(msg, chatGroup);
            }
            else
            {
                api.World.Logger.Chat(playerEntity.GetName() + " trying say: " + msg);
            }
        }

        public static void BroadcastMessage(this ICoreAPI api, string msg, int chatGroup = -1)
        {
            foreach (IPlayer player in api.World.AllOnlinePlayers)
            {
                player.SendMessage(msg, chatGroup);
            }
        }
    }
}
