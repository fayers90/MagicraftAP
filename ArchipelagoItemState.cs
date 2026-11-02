using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Archipelago.MultiClient.Net;
using BepInEx.Logging;

namespace MagicraftAP
{
    // Server-derived item state. Never reuse received items when changing seed/slot.
    public static class ArchipelagoItemState
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<int> Npcs = new HashSet<int>();
        private static bool authenticated;
        private static bool randomizeNpcs = true;
        private static int relicCaches;
        private static int startingRelics = 3;
        private static int lastReceivedCount = -1;
        private const long NpcBase = 273900100L;
        private const long RelicCache = 273900200L;
        public static bool Authenticated { get { lock (Gate) return authenticated; } }
        public static bool RandomizeNpcs { get { lock (Gate) return randomizeNpcs; } }
        public static int RelicCaches { get { lock (Gate) return relicCaches; } }
        public static int StartingRelics { get { lock (Gate) return startingRelics; } }
        public static bool IsNpcUnlocked(int npc) { lock (Gate) return authenticated && (!randomizeNpcs || Npcs.Contains(npc)); }

        public static void Reset()
        {
            lock (Gate) { authenticated = false; randomizeNpcs = true; relicCaches = 0;
                startingRelics = 3; lastReceivedCount = -1; Npcs.Clear(); }
        }
        public static void Configure(object loginResult, ManualLogSource log)
        {
            bool randomize = ReadOption(loginResult, "randomize_npcs", true);
            int starting = ReadIntOption(loginResult, "starting_relics", 3);
            lock (Gate)
            {
                Npcs.Clear(); relicCaches = 0; lastReceivedCount = -1;
                randomizeNpcs = randomize;
                startingRelics = Math.Max(3, Math.Min(77, starting));
                authenticated = true;
            }
            log.LogWarning("[MagicraftAP] AP ITEM CONFIG | Randomize NPCs: " + randomize +
                " | Starting relics: " + starting);
        }
        private static object SlotValue(object login, string key)
        {
            object slot = login.GetType().GetProperty("SlotData")?.GetValue(login, null);
            if (slot is IDictionary dict)
            {
                foreach (DictionaryEntry entry in dict)
                    if (string.Equals(entry.Key?.ToString(), key, StringComparison.OrdinalIgnoreCase)) return entry.Value;
            }
            return null;
        }
        private static bool ReadOption(object login, string key, bool fallback)
        {
            try
            {
                object v = SlotValue(login, key);
                if (v == null) return fallback;
                string t = v.ToString().Trim().Trim('"').ToLowerInvariant();
                return t == "true" || t == "1" ? true : t == "false" || t == "0" ? false : fallback;
            }
            catch { return fallback; }
        }
        private static int ReadIntOption(object login, string key, int fallback)
        {
            try { int value; return int.TryParse(SlotValue(login, key)?.ToString().Trim('"'), out value) ? value : fallback; }
            catch { return fallback; }
        }
        public static void Refresh(ArchipelagoSession session, ManualLogSource log)
        {
            try
            {
                object itemsManager = session.Items;
                PropertyInfo prop = itemsManager.GetType().GetProperty("AllItemsReceived");
                if (prop == null) { log.LogError("[MagicraftAP] Item manager missing AllItemsReceived."); return; }
                IEnumerable items = prop.GetValue(itemsManager, null) as IEnumerable;
                if (items == null) return;
                var npcs = new HashSet<int>();
                int caches = 0, count = 0;
                foreach (object item in items)
                {
                    if (item == null) continue;
                    count++;
                    object idValue = item.GetType().GetProperty("ItemId")?.GetValue(item, null);
                    if (idValue == null) continue;
                    long id = Convert.ToInt64(idValue);
                    if (id == RelicCache) caches++;
                    if (id > NpcBase && id < NpcBase + 10)
                    {
                        int npc = (int)(id - NpcBase);
                        if (npc == 1 || npc == 3 || npc == 4 || npc == 5 || npc == 6 || npc == 7 || npc == 9)
                            npcs.Add(npc);
                    }
                }
                lock (Gate)
                {
                    if (!authenticated || count == lastReceivedCount) return;
                    lastReceivedCount = count;
                    Npcs.Clear(); foreach (int npc in npcs) Npcs.Add(npc);
                    relicCaches = caches;
                }
                log.LogWarning("[MagicraftAP] AP ITEMS SYNCED | Total: " + count +
                    " | NPC unlocks: " + string.Join(",", npcs) + " | Relic caches: " + caches);
            }
            catch (Exception ex) { log.LogError("[MagicraftAP] AP item refresh failed: " + ex); }
        }
    }
}
