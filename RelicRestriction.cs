using BepInEx.Logging;
using System.Collections.Generic;
using HarmonyLib;

namespace MagicraftAP
{
    public static class RelicRestriction
    {
        // Populated by RelicPoolPatch for now.
        // Later populated using Archipelago slot data
        // and received relic unlocks.
        public static readonly HashSet<int> UnlockedRelics =
            new HashSet<int>();

        public static bool IsNormalRelic(int id)
        {
            RelicConfig config;

            if (!RelicConfig.dic.TryGetValue(id, out config))
                return false;

            if (GameConstManaged.SpecialRelicList.Contains(id))
                return false;

            return config.dropType == ItemDropType.Common ||
                   config.dropType == ItemDropType.Rare ||
                   config.dropType == ItemDropType.Epic;
        }

        public static bool IsLocked(int id)
        {
            return IsNormalRelic(id) &&
                   !UnlockedRelics.Contains(id);
        }
    }

    [HarmonyPatch(typeof(BattleData), nameof(BattleData.BackRelicToPool))]
    public static class RelicReturnPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(int id)
        {
            return !RelicRestriction.IsLocked(id);
        }
    }
}
