#nullable disable

using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace DeathCorpses.Lib.Extensions
{
    public static class ParsersExtensions
    {
        public static PlayerArgParser Player(this CommandArgumentParsers parsers, string argName, ICoreAPI api)
        {
            return new PlayerArgParser(argName, api, isMandatoryArg: true);
        }

        public class PlayerArgParser : ArgumentParserBase
        {
            protected ICoreAPI api;
            protected IPlayer player;

            public PlayerArgParser(string argName, ICoreAPI api, bool isMandatoryArg)
                : base(argName, isMandatoryArg)
            {
                this.api = api;
            }

            public override string[] GetValidRange(CmdArgs args)
            {
                return api.World.AllPlayers.Select((IPlayer p) => p.PlayerName).ToArray();
            }

            public override object GetValue()
            {
                return player;
            }

            public override void SetValue(object data)
            {
                player = (IPlayer)data;
            }

            public override EnumParseResult TryProcess(TextCommandCallingArgs args, Action<AsyncParseResults> onReady = null)
            {
                string playername = args.RawArgs.PopWord();
                if (playername == null)
                {
                    lastErrorMessage = Lang.Get("Argument is missing");
                    return EnumParseResult.Bad;
                }

                player = api.World.AllPlayers.FirstOrDefault((IPlayer p) => p.PlayerName == playername);
                if (player == null)
                {
                    lastErrorMessage = Lang.Get("No such player");
                    return EnumParseResult.Bad;
                }

                return EnumParseResult.Good;
            }
        }
    }
}
