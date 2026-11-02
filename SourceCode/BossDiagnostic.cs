
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace MagicraftAP
{
    [HarmonyPatch(typeof(RoomController), nameof(RoomController.OnRoomFinish))]
    public static class BossDiagnostic
    {
        public static Action<string> OnEliteCleared;
        private static readonly ManualLogSource Log =
            new ManualLogSource("MagicraftAP.EliteChecks");

        private static readonly AccessTools.FieldRef<RoomController, RoomConfig>
            RoomConfigField =
                AccessTools.FieldRefAccess<RoomController, RoomConfig>("roomCfg");

        private static readonly HashSet<string> CheckedElites =
            new HashSet<string>(StringComparer.Ordinal);

        private static string SavePath =>
            Path.Combine(
                SaveRedirectPatch.ProfilePath,
                "ModData",
                "EliteChecks.txt"
            );

        private static bool loaded;

        static BossDiagnostic()
        {
            BepInEx.Logging.Logger.Sources.Add(Log);
        }

        private static void LoadChecks()
        {
            if (loaded)
                return;

            loaded = true;

            try
            {
                if (!File.Exists(SavePath))
                    return;

                foreach (string line in File.ReadAllLines(SavePath))
                {
                    string key = line.Trim();

                    if (!string.IsNullOrEmpty(key))
                        CheckedElites.Add(key);
                }

                Log.LogInfo(
                    "[MagicraftAP] Loaded " +
                    CheckedElites.Count +
                    " previously checked elites."
                );
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "[MagicraftAP] Failed to load elite checks: " + ex
                );
            }
        }

        private static bool SaveChecks()
        {
            try
            {
                var keys = new List<string>(CheckedElites);
                keys.Sort(StringComparer.Ordinal);

                Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
                File.WriteAllLines(SavePath, keys);
                return true;
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "[MagicraftAP] Failed to save elite checks: " + ex
                );

                return false;
            }
        }

        [HarmonyPrefix]
        public static void Prefix(
            RoomController __instance,
            out bool __state)
        {
            __state = !__instance.IsFinish;
        }

        [HarmonyPostfix]
        public static void Postfix(
            RoomController __instance,
            bool __state)
        {
            if (!__state || !__instance.IsFinish)
                return;

            try
            {
                RoomConfig room = RoomConfigField(__instance);

                // Only process elite rooms.
                if (room.type != RoomType.Elite)
                    return;

                LoadChecks();

                BattleData battle = PlayerMgr.Inst.BaData;

                string difficulty =
                    DataMgr.selectedWorldData.selectedDifficulty.ToString();

                var unitIds = new HashSet<int>();

                if (room.allObjList != null)
                {
                    foreach (var wave in room.allObjList)
                    {
                        if (wave == null)
                            continue;

                        foreach (RoomObjData obj in wave)
                        {
                            if (obj != null &&
                                obj.objType == RoomObjType.Unit)
                            {
                                unitIds.Add(obj.id);
                            }
                        }
                    }
                }

                if (UnitConfig.list == null)
                {
                    Log.LogWarning(
                        "[MagicraftAP] UnitConfig.list unavailable."
                    );
                    return;
                }

                var eliteUnits = new Dictionary<int, string>();

                foreach (UnitConfig unit in UnitConfig.list)
                {
                    if (!unitIds.Contains(unit.id))
                        continue;

                    if (unit.unitType != UnitType.Elite)
                        continue;

                    string name;

                    try
                    {
                        name = unit.GetName();
                    }
                    catch
                    {
                        name = "Unknown Elite";
                    }

                    if (string.IsNullOrWhiteSpace(name))
                        name = "Elite " + unit.id;

                    eliteUnits[unit.id] = name;
                }

                if (eliteUnits.Count == 0)
                {
                    Log.LogWarning(
                        "[MagicraftAP] ELITE ROOM WITHOUT IDENTIFIED ELITE" +
                        " | Difficulty: " + difficulty +
                        " | Stage: " + battle.currentStage +
                        " | Level: " + battle.currentLevel +
                        " | RoomID: " + room.id
                    );

                    return;
                }

                foreach (var elite in eliteUnits)
                {
                    int unitId = elite.Key;
                    string eliteName = elite.Value;

                    string checkKey =
                        difficulty + ":Elite:" + unitId;

                    string locationName =
                        difficulty + " - Elite - " + eliteName;

                    Log.LogWarning("[MagicraftAP] ELITE CLEAR DETECTED | " + locationName + " | UnitID: " + unitId);
                    try { OnEliteCleared?.Invoke(locationName); }
                    catch (Exception ex) { Log.LogError("[MagicraftAP] Elite AP callback failed: " + ex); }

                }
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "[MagicraftAP] Elite check processing failed: " + ex
                );
            }
        }
    }
}
