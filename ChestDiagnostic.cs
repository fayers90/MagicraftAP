using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Collections;
using Unity.Entities;

namespace MagicraftAP
{
    public static class ChestDiagnostic
    {
        private static readonly ManualLogSource Log = CreateLogger();
        private static readonly HashSet<string> CountedChests = new HashSet<string>(StringComparer.Ordinal);
        // Records the last observed open state, rather than just whether the entity was seen.
        private static readonly Dictionary<string, bool> ObservedChests = new Dictionary<string, bool>(StringComparer.Ordinal);
        private static readonly HashSet<string> ReportedSystems = new HashSet<string>(StringComparer.Ordinal);
        private static int totalOpened;
        public static Action OnChestOpened;

        private static ManualLogSource CreateLogger()
        {
            var log = new ManualLogSource("MagicraftAP.Chests");
            BepInEx.Logging.Logger.Sources.Add(log);
            return log;
        }
        public static void LogInfo(string message) { Log.LogWarning("[MagicraftAP] " + message); }
        public static void LogPatchError(Exception ex) { Log.LogError("[MagicraftAP] Chest system patch failed: " + ex); }
        public static void ReportSystemExecution(string systemName)
        {
            if (ReportedSystems.Add(systemName)) LogInfo("CHEST PATCH EXECUTING: " + systemName);
        }
        private static string GetEntityKey(EntityManager manager, Entity entity)
        {
            return manager.WorldUnmanaged.SequenceNumber + ":" + entity.Index + ":" + entity.Version;
        }
        private static void RecordChest(string key, string chestType)
        {
            if (!CountedChests.Add(key)) return;
            totalOpened++;
            string difficulty = "Unknown";
            int stage = -1, level = -1;
            try
            {
                difficulty = DataMgr.selectedWorldData.selectedDifficulty.ToString();
                BattleData battle = PlayerMgr.Inst.BaData;
                stage = battle.currentStage;
                level = battle.currentLevel;
            }
            catch (Exception ex) { Log.LogWarning("[MagicraftAP] Could not read room context: " + ex.Message); }
            Log.LogWarning("[MagicraftAP] CHEST OPENED | Type: " + chestType +
                " | Difficulty: " + difficulty + " | Stage: " + stage + " | Level: " + level +
                " | Entity: " + key + " | Session Total: " + totalOpened);
            try { if (OnChestOpened != null) OnChestOpened(); }
            catch (Exception ex) { Log.LogError("[MagicraftAP] Chest AP callback failed: " + ex); }
        }
        private static void ObserveChest(EntityManager manager, Entity entity, bool alreadyOpen, string chestType)
        {
            string key = GetEntityKey(manager, entity);
            bool previous;
            if (!ObservedChests.TryGetValue(key, out previous))
            {
                // First sighting establishes baseline; don't count already-open chests.
                ObservedChests[key] = alreadyOpen;
                return;
            }
            ObservedChests[key] = alreadyOpen;
            if (!previous && alreadyOpen) RecordChest(key, chestType);
        }
        public static void ScanChests(EntityManager manager, bool noLockSystem)
        {
            try
            {
                if (noLockSystem)
                {
                    using (EntityQuery query = manager.CreateEntityQuery(ComponentType.ReadOnly<SpecialObj4NoLock>()))
                    using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
                        foreach (Entity entity in entities)
                        {
                            SpecialObj4NoLock chest = manager.GetComponentData<SpecialObj4NoLock>(entity);
                            ObserveChest(manager, entity, chest.alreadyOpen, "NoLock");
                        }
                }
                else
                {
                    using (EntityQuery query = manager.CreateEntityQuery(ComponentType.ReadOnly<SpecialObj4_Dots>()))
                    using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
                        foreach (Entity entity in entities)
                        {
                            SpecialObj4_Dots chest = manager.GetComponentData<SpecialObj4_Dots>(entity);
                            ObserveChest(manager, entity, chest.alreadyOpen, chest.chestType.ToString());
                        }
                }
            }
            catch (Exception ex) { Log.LogError("[MagicraftAP] Chest scan failed: " + ex); }
        }
    }

    [HarmonyPatch]
    public static class ChestSystemPatch
    {
        private static readonly PropertyInfo EntityManagerProperty =
            AccessTools.Property(typeof(ComponentSystemBase), "EntityManager");
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            string[] systemNames = { "SpecialObj4System", "SpecialObj4NoLockSystem" };
            foreach (string systemName in systemNames)
            {
                Type systemType = AccessTools.TypeByName(systemName);
                if (systemType == null)
                {
                    ChestDiagnostic.LogInfo("CHEST PATCH TARGET NOT FOUND: " + systemName);
                    continue;
                }
                MethodInfo updateMethod = AccessTools.Method(systemType, "OnUpdate");
                if (updateMethod == null)
                {
                    ChestDiagnostic.LogInfo("CHEST UPDATE METHOD NOT FOUND: " + systemName);
                    continue;
                }
                ChestDiagnostic.LogInfo("CHEST PATCH TARGET FOUND: " + systemType.FullName + "." + updateMethod.Name);
                yield return updateMethod;
            }
        }
        [HarmonyPostfix]
        public static void Postfix(object __instance)
        {
            try
            {
                string systemName = __instance.GetType().Name;
                ChestDiagnostic.ReportSystemExecution(systemName);
                if (EntityManagerProperty == null)
                {
                    ChestDiagnostic.LogInfo("CHEST ERROR: EntityManager property not found");
                    return;
                }
                EntityManager manager = (EntityManager)EntityManagerProperty.GetValue(__instance);
                ChestDiagnostic.ScanChests(manager, systemName == "SpecialObj4NoLockSystem");
            }
            catch (Exception ex) { ChestDiagnostic.LogPatchError(ex); }
        }
    }
}
