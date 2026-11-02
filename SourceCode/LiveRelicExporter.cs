
using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace MagicraftAP
{
    [HarmonyPatch(typeof(BattleData), nameof(BattleData.Initialize))]
    public static class LiveRelicExporter
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("MagicraftAP.LiveRelics");

        [HarmonyPostfix]
        public static void Export(BattleData __instance)
        {
            try
            {
                string path = Path.Combine(
                    Paths.PluginPath,
                    "MagicraftAP_LiveRelics.csv"
                );

                var lines = new[]
                {
                    "ID,RemainingCount,MaxCount,DropType,NeedActivate"
                }
                .Concat(
                    __instance.poolOfRelic
                        .OrderBy(kv => kv.Key)
                        .Select(kv =>
                        {
                            RelicConfig relic;

                            if (RelicConfig.dic.TryGetValue(
                                kv.Key, out relic))
                            {
                                return string.Format(
                                    "{0},{1},{2},{3},{4}",
                                    kv.Key,
                                    kv.Value,
                                    relic.maxCount,
                                    relic.dropType,
                                    relic.needActivate
                                );
                            }

                            return string.Format(
                                "{0},{1},Unknown,Unknown,Unknown",
                                kv.Key,
                                kv.Value
                            );
                        })
                );

                File.WriteAllLines(path, lines);

                Log.LogInfo(
                    "[MagicraftAP] Exported live relic pool: " + path
                );
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "[MagicraftAP] Live relic export failed: " + ex
                );
            }
        }
    }
}
