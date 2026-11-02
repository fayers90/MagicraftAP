
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace MagicraftAP
{
    [HarmonyPatch]
    public static class NPCUnlockChecks
    {
        public static Action<int> OnNpcUnlocked;
        private static readonly ManualLogSource Log =
            new ManualLogSource("MagicraftAP.NPCUnlocks");

        private static readonly Dictionary<int, string> NpcNames =
            new Dictionary<int, string>
            {
                { 3, "NPC3 - Research" },
                { 4, "NPC4" },
                { 5, "NPC5" },
                { 6, "NPC6 - Spell Activation" }
            };

        private static readonly HashSet<int> Completed =
            new HashSet<int>();

        private static string SavePath =>
            Path.Combine(SaveRedirectPatch.ProfilePath,
                "ModData", "NPCUnlocks.txt");

        private static bool initialized;

        static NPCUnlockChecks()
        {
            BepInEx.Logging.Logger.Sources.Add(Log);
        }

        [HarmonyTargetMethod]
        private static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName("CampMgr");

            if (type == null)
            {
                Log.LogError("[MagicraftAP] CampMgr not found.");
                return null;
            }

            MethodInfo method = AccessTools.Method(type, "ShowCampObj");

            if (method == null)
            {
                Log.LogError("[MagicraftAP] CampMgr.ShowCampObj not found.");
                return null;
            }

            Log.LogWarning(
                "[MagicraftAP] NPC UNLOCK PATCH TARGET FOUND: CampMgr.ShowCampObj");

            return method;
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            try
            {
                Initialize();

                Type dataMgrType = AccessTools.TypeByName("DataMgr");

                if (dataMgrType == null)
                    return;

                object world = AccessTools.Property(
                    dataMgrType, "selectedWorldData")
                    ?.GetValue(null, null);

                if (world == null)
                {
                    FieldInfo worldField = AccessTools.Field(
                        dataMgrType, "selectedWorldData");

                    world = worldField?.GetValue(null);
                }

                if (world == null)
                    return;

                Check(world, 3, "story2Finish");
                Check(world, 4, "story3Finish");
                Check(world, 5, "story4NPC5Rescued");
                Check(world, 6, "storyKillChapter3BossPickup");
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "[MagicraftAP] NPC unlock diagnostic error: " + ex);
            }
        }

        private static void Initialize()
        {
            if (initialized)
                return;

            initialized = true;

            try
            {
                if (File.Exists(SavePath))
                {
                    foreach (string line in File.ReadAllLines(SavePath))
                    {
                        int npcId;

                        if (int.TryParse(line.Trim(), out npcId))
                            Completed.Add(npcId);
                    }
                }

                Log.LogInfo(
                    "[MagicraftAP] Loaded " + Completed.Count +
                    " previously checked NPC unlocks.");
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "[MagicraftAP] Failed loading NPC unlocks: " + ex);
            }
        }

        private static void Check(
            object world,
            int npcId,
            string flagName)
        {
            FieldInfo field = AccessTools.Field(
                world.GetType(), flagName);

            if (field == null)
            {
                Log.LogWarning(
                    "[MagicraftAP] NPC flag not found: " + flagName);
                return;
            }

            object value = field.GetValue(world);

            if (!(value is bool) || !(bool)value)
                return;

            string name = NpcNames.ContainsKey(npcId) ? NpcNames[npcId] : "NPC" + npcId;
            Log.LogWarning("[MagicraftAP] NPC UNLOCK DETECTED | " + name + " | Flag: " + flagName);
            try { OnNpcUnlocked?.Invoke(npcId); }
            catch (Exception ex) { Log.LogError("[MagicraftAP] NPC AP callback failed: " + ex); }

        }
    }
}
