using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace DeathCorpses.Items
{
    /// <summary>
    /// Faded obituary — the corpse this obituary was bound to has been collected or destroyed.
    /// Purely a collectible keepsake. No server-side corpse checks, no particles.
    /// </summary>
    public class ItemObituariesFaded : Item
    {
        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            string? ownerName = inSlot.Itemstack.Attributes.GetString("obituaryOwnerName");
            if (ownerName != null)
            {
                dsc.AppendLine(Lang.Get($"{Constants.ModId}:obituaries-faded-desc", ownerName));
            }
        }
    }
}
