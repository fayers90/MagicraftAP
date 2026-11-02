
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace MagicraftAP
{
    internal static class NPCDiagnostic
    {
        private static readonly ManualLogSource Log = CreateLogger();

        private static readonly string SavePath = Path.Combine(
            Paths.ConfigPath,
            "MagicraftAP_TestNPCChecks.txt"
        );

        private static readonly HashSet<string> CompletedChecks =
            new HashSet<string>(StringComparer.Ordinal);

        private static bool loaded;

        private static ManualLogSource CreateLogger()
        {
            var log = new ManualLogSource("MagicraftAP.NPCChecks");
            BepInEx.Logging.Logger.Sources.Add(log);
            return log;
        }

        private static void Load()
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
                        string key = line.Trim();

                        if (key.Length > 0)
                            CompletedChecks.Add(key);
                    }
                }

                Log.LogInfo(
                    "[MagicraftAP] NPC diagnostics loaded. " +
                    "Existing checks: " + CompletedChecks.Count
                );
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "[MagicraftAP] Failed to load NPC checks: " + ex
                );
            }
        }

        internal static void RecordCheck(string location)
        {
            Load();

            if (!CompletedChecks.Add(location))
            {
                Log.LogInfo(
                    "[MagicraftAP] NPC LOCATION ALREADY CHECKED | " +
                    location
                );
                return;
            }

            try
            {
                File.AppendAllText(
                    SavePath,
                    location + Environment.NewLine
                );
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "[MagicraftAP] Failed to save NPC check: " + ex
                );
            }

            Log.LogWarning(
                "[MagicraftAP] NEW NPC LOCATION CHECK | " +
                location +
                " | Total NPC Checks: " + CompletedChecks.Count
            );
        }

        internal static void LogService(string npc, string service)
        {
            Log.LogInfo(
                "[MagicraftAP] NPC SERVICE ACCESSED | " +
                npc + " | " + service
            );
        }

        internal static void LogProgress(string message)
        {
            Log.LogInfo("[MagicraftAP] NPC PROGRESSION | " + message);
        }
    }

    // ============================================================
    // NPC SERVICES
    // These patches only log access.
    // They never block the original methods.
    // ============================================================

    [HarmonyPatch(typeof(NPC1Vivian), nameof(NPC1Vivian.UseNPCFunction))]
    internal static class NPC1ServiceDiagnostic
    {
        private static void Prefix()
        {
            NPCDiagnostic.LogService("NPC1 - Vivian", "Talents");
        }
    }

    [HarmonyPatch(typeof(NPC3), nameof(NPC3.UseNPCFunction))]
    internal static class NPC3ServiceDiagnostic
    {
        private static void Prefix()
        {
            NPCDiagnostic.LogService("NPC3", "Research");
        }
    }

    [HarmonyPatch(typeof(NPC4), nameof(NPC4.UseNPCFunction))]
    internal static class NPC4ServiceDiagnostic
    {
        private static void Prefix()
        {
            NPCDiagnostic.LogService("NPC4", "Sets");
        }
    }

    [HarmonyPatch(typeof(NPC5), nameof(NPC5.UseNPCFunction))]
    internal static class NPC5ServiceDiagnostic
    {
        private static void Prefix()
        {
            NPCDiagnostic.LogService("NPC5", "Training");
        }
    }

    [HarmonyPatch(typeof(NPC6), nameof(NPC6.UseNPCFunction))]
    internal static class NPC6ServiceDiagnostic
    {
        private static void Prefix()
        {
            NPCDiagnostic.LogService("NPC6", "Spell Activation");
        }
    }

    [HarmonyPatch(typeof(NPC7), nameof(NPC7.UseNPCFunction))]
    internal static class NPC7ServiceDiagnostic
    {
        private static void Prefix()
        {
            NPCDiagnostic.LogService("NPC7", "Spell Disabling");
        }
    }

    [HarmonyPatch(
        typeof(NPC9_EndlessGuide),
        nameof(NPC9_EndlessGuide.UseNPCFunction)
    )]
    internal static class NPC9ServiceDiagnostic
    {
        private static void Prefix()
        {
            NPCDiagnostic.LogService(
                "NPC9 - Endless Guide",
                "Endless Talents"
            );
        }
    }

    // ============================================================
    // NPC4 RESCUE
    // Only record the check when the vanilla rescue flag
    // changes from false to true.
    // ============================================================

    [HarmonyPatch(typeof(NPC4_Trapped), nameof(NPC4_Trapped.Interact))]
    internal static class NPC4RescueDiagnostic
    {
        private static void Prefix(ref bool __state)
        {
            __state =
                DataMgr.selectedWorldData != null &&
                DataMgr.selectedWorldData.story3NPC4Rescued;
        }

        private static void Postfix(bool __state)
        {
            if (DataMgr.selectedWorldData == null)
                return;

            bool rescued =
                DataMgr.selectedWorldData.story3NPC4Rescued;

            if (!__state && rescued)
            {
                NPCDiagnostic.RecordCheck("Rescue NPC4");
            }
        }
    }

    // ============================================================
    // NPC5 RESCUE
    // ============================================================

    [HarmonyPatch(typeof(NPC5_Trapped), nameof(NPC5_Trapped.Interact))]
    internal static class NPC5RescueDiagnostic
    {
        private static void Prefix(ref bool __state)
        {
            __state =
                DataMgr.selectedWorldData != null &&
                DataMgr.selectedWorldData.story4NPC5Rescued;
        }

        private static void Postfix(bool __state)
        {
            if (DataMgr.selectedWorldData == null)
                return;

            bool rescued =
                DataMgr.selectedWorldData.story4NPC5Rescued;

            if (!__state && rescued)
            {
                NPCDiagnostic.RecordCheck("Rescue NPC5");
            }
        }
    }

    // ============================================================
    // NPC7 APPEARANCE
    // The appearance manager sets the story flag in Start().
    // We log the transition but do not award a location yet.
    // ============================================================

    [HarmonyPatch(typeof(NPC7AppearanceMgr), "Start")]
    internal static class NPC7AppearanceDiagnostic
    {
        private static void Prefix(ref bool __state)
        {
            __state =
                DataMgr.selectedWorldData != null &&
                DataMgr.selectedWorldData.storyHardFinishNPC7Appearance;
        }

        private static void Postfix(bool __state)
        {
            if (DataMgr.selectedWorldData == null)
                return;

            bool appeared =
                DataMgr.selectedWorldData.storyHardFinishNPC7Appearance;

            if (!__state && appeared)
            {
                NPCDiagnostic.LogProgress(
                    "NPC7 appearance sequence started"
                );
            }
        }
    }

    // ============================================================
    // NPC7 SERVICE UNLOCK
    // Monitor progression during NPC7.Initialize().
    // This is diagnostic only.
    // ============================================================

    [HarmonyPatch(typeof(NPC7), nameof(NPC7.Initialize))]
    internal static class NPC7UnlockDiagnostic
    {
        private static bool? lastKnownUnlockState;

        private static void Postfix()
        {
            if (DataMgr.selectedWorldData == null)
                return;

            bool unlocked =
                DataMgr.selectedWorldData.storyHardFinishNPC7OpenFunction;

            if (lastKnownUnlockState.HasValue &&
                lastKnownUnlockState.Value == unlocked)
            {
                return;
            }

            lastKnownUnlockState = unlocked;

            NPCDiagnostic.LogProgress(
                "NPC7 Spell Disabling story unlock = " + unlocked
            );
        }
    }
}
