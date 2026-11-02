
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
    public static class NPCServiceGate
    {
        private static readonly ManualLogSource Log =
            new ManualLogSource("MagicraftAP.NPCGate");

        private static readonly string UnlockFile =
            Path.Combine(
                Paths.ConfigPath,
                "MagicraftAP_NPCItems.txt"
            );

        private static readonly Dictionary<int, string> Services =
            new Dictionary<int, string>
            {
                { 1, "Talents" },
                { 3, "Research" },
                { 4, "Sets" },
                { 5, "Training" },
                { 6, "Spell Activation" },
                { 7, "Spell Disabling" },
                { 9, "Endless Talents" }
            };

        static NPCServiceGate()
        {
            BepInEx.Logging.Logger.Sources.Add(Log);
        }

        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var targets = new Dictionary<int, string>
            {
                { 1, "NPC1Vivian" },
                { 3, "NPC3" },
                { 4, "NPC4" },
                { 5, "NPC5" },
                { 6, "NPC6" },
                { 7, "NPC7" },
                { 9, "NPC9_EndlessGuide" }
            };

            foreach (var target in targets)
            {
                Type type = AccessTools.TypeByName(target.Value);

                if (type == null)
                {
                    Log.LogError(
                        "[MagicraftAP] NPC type missing: " +
                        target.Value);
                    continue;
                }

                MethodInfo method =
                    AccessTools.DeclaredMethod(
                        type,
                        "UseNPCFunction");

                if (method == null)
                {
                    Log.LogError(
                        "[MagicraftAP] NPC method missing: " +
                        target.Value + ".UseNPCFunction");
                    continue;
                }

                Log.LogWarning(
                    "[MagicraftAP] NPC GATE TARGET FOUND | NPC" +
                    target.Key + " | " + Services[target.Key]);

                yield return method;
            }
        }

        [HarmonyPrefix]
        private static bool Prefix(MethodBase __originalMethod)
        {
            try
            {
                int npcId = GetNpcId(
                    __originalMethod.DeclaringType.Name);

                if (npcId == 0)
                    return true;

                bool unlocked = IsUnlocked(npcId);

                string service = Services[npcId];

                if (unlocked)
                {
                    Log.LogWarning(
                        "[MagicraftAP] NPC" + npcId +
                        " ALLOWED | " + service +
                        " | AP item received");

                    return true;
                }

                Log.LogWarning(
                    "[MagicraftAP] NPC" + npcId +
                    " BLOCKED | " + service +
                    " | Requires AP unlock item");

                return false;
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "[MagicraftAP] NPC gate error: " + ex);

                // Fail open if our diagnostic fails.
                return true;
            }
        }

        private static int GetNpcId(string typeName)
        {
            switch (typeName)
            {
                case "NPC1Vivian": return 1;
                case "NPC3": return 3;
                case "NPC4": return 4;
                case "NPC5": return 5;
                case "NPC6": return 6;
                case "NPC7": return 7;
                case "NPC9_EndlessGuide": return 9;
                default: return 0;
            }
        }

        private static bool IsUnlocked(int npcId)
        {
            // Until authenticated, never trust stale local test files.
            return ArchipelagoItemState.IsNpcUnlocked(npcId);
        }
    }
}
