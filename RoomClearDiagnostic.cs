
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace MagicraftAP
{
    [HarmonyPatch(typeof(RoomController), "OnRoomFinish")]
    public static class RoomClearDiagnostic
    {
        private static readonly ManualLogSource Log = CreateLogger();
        public static Action<string> OnRoomCleared;

        private static readonly HashSet<string> CheckedLocations =
            new HashSet<string>(StringComparer.Ordinal);

        private static string SavePath => Path.Combine(
            SaveRedirectPatch.ProfilePath,
            "ModData",
            "RoomChecks.txt"
        );

        private static bool loaded;

        private static ManualLogSource CreateLogger()
        {
            var log = new ManualLogSource("MagicraftAP.RoomClear");
            BepInEx.Logging.Logger.Sources.Add(log);
            return log;
        }

        private static void LoadChecks()
        {
            if (loaded)
                return;

            loaded = true;

            try
            {
                if (File.Exists(SavePath))
                {
                    foreach (string line in File.ReadAllLines(SavePath))
                    {
                        string location = line.Trim();

                        if (!string.IsNullOrEmpty(location))
                            CheckedLocations.Add(location);
                    }
                }

                Log.LogInfo(
                    "[MagicraftAP] Loaded " +
                    CheckedLocations.Count +
                    " previously checked rooms."
                );
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "[MagicraftAP] Failed to load room checks: " + ex
                );
            }
        }

        [HarmonyPrefix]
        public static void Prefix(
            RoomController __instance,
            out bool __state
        )
        {
            __state = !__instance.IsFinish;
        }

        [HarmonyPostfix]
        public static void Postfix(
            RoomController __instance,
            bool __state
        )
        {
            if (!__state || !__instance.IsFinish)
                return;

            try
            {
                LoadChecks();

                BattleData battle = PlayerMgr.Inst.BaData;

                string difficulty =
                    DataMgr.selectedWorldData.selectedDifficulty.ToString();

                int stage = battle.currentStage;
                int level = battle.currentLevel;
                int roomID = battle.currentRoomID;

                // Keep the game's actual level value for now.
                // Stage 1 starts at 0, but Stage 2 was observed at 1.
                string location =
                    difficulty +
                    " - Stage " + stage +
                    " - Level " + level;

                Log.LogWarning("[MagicraftAP] ROOM CLEAR DETECTED | " + location + " | RoomID: " + roomID);
                try { OnRoomCleared?.Invoke(location); }
                catch (Exception ex) { Log.LogError("[MagicraftAP] Room AP callback failed: " + ex); }

            }
            catch (Exception ex)
            {
                Log.LogError(
                    "[MagicraftAP] Room tracking failed: " + ex
                );
            }
        }
    }
}
