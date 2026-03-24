using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace DeathCorpses.Lib.Utils
{
    public static class WorldUtil
    {
        /// <summary>
        /// Converts absolute coordinates to coordinates relative to the world spawn
        /// </summary>
        public static Vec3d RelativePos(this Vec3d pos, ICoreAPI api)
        {
            pos.X -= api.World.DefaultSpawnPosition.XYZ.X;
            pos.Z -= api.World.DefaultSpawnPosition.XYZ.Z;
            return pos;
        }
    }
}
