
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;

namespace MagicraftAP
{
    [HarmonyPatch(typeof(BattleData), nameof(BattleData.Initialize))]
    public static class RelicPoolPatch
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("MagicraftAP.RelicPool");

        private static readonly HashSet<int> StartingRelics =
            new HashSet<int>();

        private static int appliedStarting = -1;
        private static int appliedCaches = -1;

        [HarmonyPostfix]
        public static void Apply(BattleData __instance)
        {
            try
            {
                int start = ArchipelagoItemState.StartingRelics;
                int caches = ArchipelagoItemState.RelicCaches;
                if (appliedStarting != start || appliedCaches != caches)
                {
                    StartingRelics.Clear();
                    var rng = new System.Random(12345);
                    var candidates = RelicConfig.list
                        .Where(r => RelicRestriction.IsNormalRelic(r.id) && IsEligibleForNormalPool(r.id))
                        .Select(r => r.id).Distinct().OrderBy(id => id).ToList();
                    foreach (ItemDropType rarity in new[] { ItemDropType.Common, ItemDropType.Rare, ItemDropType.Epic })
                    {
                        var tier = candidates.Where(id => RelicConfig.dic[id].dropType == rarity).ToList();
                        if (tier.Count > 0) StartingRelics.Add(tier[rng.Next(tier.Count)]);
                    }
                    // Each cache adds one deterministic additional relic to the available pool.
                    int wanted = Math.Min(candidates.Count, Math.Max(3, start) + caches);
                    foreach (int id in candidates.OrderBy(id => id))
                    {
                        if (StartingRelics.Count >= wanted) break;
                        StartingRelics.Add(id);
                    }
                    RelicRestriction.UnlockedRelics.Clear();
                    foreach (int id in StartingRelics) RelicRestriction.UnlockedRelics.Add(id);
                    appliedStarting = start; appliedCaches = caches;
                    Log.LogWarning("[MagicraftAP] AP RELIC POOL | Starting: " + start +
                        " | Received caches: " + caches + " | Unlocked: " + StartingRelics.Count);
                }

                int removed = 0;
                int added = 0;

                foreach (int id in __instance.poolOfRelic.Keys.ToList())
                {
                    if (RelicRestriction.IsLocked(id))
                    {
                        __instance.poolOfRelic.Remove(id);
                        removed++;
                    }
                }

                foreach (int id in RelicRestriction.UnlockedRelics)
                {
                    if (!RelicRestriction.IsNormalRelic(id))
                        continue;

                    if (!IsEligibleForNormalPool(id))
                        continue;

                    if (__instance.poolOfRelic.ContainsKey(id))
                        continue;

                    RelicConfig relic;

                    if (!RelicConfig.dic.TryGetValue(id, out relic))
                        continue;

                    if (relic.maxCount <= 0)
                        continue;

                    __instance.poolOfRelic.Add(id, relic.maxCount);
                    added++;
                }

                Log.LogInfo(
                    "[MagicraftAP] Pool filtered. Removed: " +
                    removed +
                    ", Added: " +
                    added +
                    ", Remaining: " +
                    __instance.poolOfRelic.Count
                );

                Log.LogInfo(
                    "[MagicraftAP] Available relics: " +
                    string.Join(", ",
                        __instance.poolOfRelic.Keys.OrderBy(id => id))
                );
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "[MagicraftAP] Relic pool patch failed: " + ex
                );
            }
        }

        private static bool IsEligibleForNormalPool(int id)
        {
            RelicConfig relic;

            if (!RelicConfig.dic.TryGetValue(id, out relic))
                return false;

            if (!RelicRestriction.IsNormalRelic(id))
                return false;

            switch (id)
            {
                case 31:
                case 32:
                case 72:
                case 76:
                case 79:
                    return false;
            }

            if (relic.abilityType ==
                    RelicAbilityType.PostSlotMoreEfficiency &&
                DataMgr.selectedWorldData.enterBattleTime <= 1)
            {
                return false;
            }

            if (id == 69 &&
                DataMgr.selectedWorldData.enterBattleTime <= 1)
            {
                return false;
            }

            return true;
        }
    }
}
