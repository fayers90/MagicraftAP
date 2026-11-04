using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using Archipelago.MultiClient.Net;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace MagicraftAP
{
    // Experimental native shelf integration. AP stock is attached to REAL ECS shop
    // entities, not a separate purchase window. No ItemType is invented.
    public static class ArchipelagoShop
    {
        private static ArchipelagoClient client;
        private static ManualLogSource log;
        private static readonly Dictionary<string, long> stock = new Dictionary<string, long>();
        private static readonly HashSet<long> assigned = new HashSet<long>();
        private static readonly HashSet<long> bought = new HashSet<long>();
        private static readonly Dictionary<long, string> labels = new Dictionary<long, string>();
        private static readonly Dictionary<long, string> itemKinds = new Dictionary<long, string>();
        private static readonly object labelLock = new object();
        private static readonly Dictionary<string, TextMesh> signs = new Dictionary<string, TextMesh>();
        private static int purchaseCount;
        // A visit is identified by its seven physical shelf entity IDs, not a
        // room coordinate (coordinates are reused between runs).
        private static string activeVisit;
        private static readonly HashSet<string> selectedShelves = new HashSet<string>();
        private static readonly HashSet<string> seenShelfKeys = new HashSet<string>();
        private static int visitSequence;
        private static int diagnosticTicks;
        private static string lastGate;
        private static bool hookEntered;
        private static string lastDiagnostic;
        private static Texture2D apLogo;
        private static bool logoAttempted;
        private static readonly HashSet<string> iconApplied = new HashSet<string>();
        public static void Initialize(ArchipelagoClient ap, ManualLogSource logger) { client = ap; log = logger; }
        public static void Update() { /* Native shelf only. No F9. */ }
        public static void OnGUI() { }
        public static void NotifyShopSeen() { }
        public static void ClearLabels()
        {
            lock (labelLock) { labels.Clear(); itemKinds.Clear(); }
            stock.Clear(); assigned.Clear(); bought.Clear(); selectedShelves.Clear(); seenShelfKeys.Clear(); activeVisit = null; visitSequence = 0; purchaseCount = 0; diagnosticTicks = 0; lastDiagnostic = null; lastGate = null;
            foreach (var sign in signs.Values) if (sign != null) UnityEngine.Object.Destroy(sign.gameObject);
            signs.Clear(); iconApplied.Clear();
        }
        public static void SetScoutedLabel(long id, string name) { lock (labelLock) labels[id] = name ?? "Archipelago Item"; }
        private static string Kind(long id) { lock (labelLock) return itemKinds.ContainsKey(id) ? itemKinds[id] : "filler"; }
        private static string Label(long id) { lock (labelLock) return labels.ContainsKey(id) ? labels[id] : "Archipelago Item"; }
        private static string Key(EntityManager manager, Entity entity) { return manager.WorldUnmanaged.SequenceNumber + ":" + entity.Index + ":" + entity.Version; }

        // Called BEFORE ItemSystem's native purchase branch. Consumes onInteract
        // so vanilla payment/reward cannot also run. If this hook is not called,
        // we do not assign any AP merchandise (fail closed).
        public static void BeforeNativeItemUpdate()
        {
            if (!hookEntered)
            {
                hookEntered = true;
                ShopLog("[MagicraftAP] SHOP HOOK ENTERED | ItemSystem.OnUpdate prefix executing");
            }
            string gate = null;
            if (client == null) gate = "client null";
            else if (!client.IsConnected) gate = "client disconnected";
            else if (PlayerMgr.Inst == null) gate = "PlayerMgr null";
            else if (PlayerMgr.Inst.BaData == null) gate = "BaData null";
            if (gate != null) { Gate(gate); return; }
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) { Gate("default ECS world unavailable"); return; }
            EntityManager manager = world.EntityManager;
            long[] locations = client.GetShopLocations();
            if (locations.Length == 0) { Gate("no shop locations in cache"); return; }
            if (client.MidRunShopApItems <= 0) { Gate("AP shelves configured to zero"); return; }
            Gate("ready: " + locations.Length + " locations, " + client.MidRunShopApItems + " requested");
            if (++diagnosticTicks == 1) log.LogWarning("[MagicraftAP] SHOP HOOK ACTIVE | locations=" + locations.Length + " | requested=" + client.MidRunShopApItems);
            using (EntityQuery query = manager.CreateEntityQuery(ComponentType.ReadOnly<Item>(), ComponentType.ReadWrite<InteractiveObj_Dots>()))
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            {
                // Discover the physical shelves, including the guaranteed wand.
                // Do not filter out already-purchased vanilla shelves when
                // identifying a visit; a purchase must not reshuffle AP stock.
                var shelves = new List<Entity>();
                foreach (Entity candidate in entities)
                {
                    Item ci = manager.GetComponentData<Item>(candidate);
                    if (ci.isStore && ci.isInitialized && !ci.onPick)
                        shelves.Add(candidate);
                }
                // Log the actual ECS state even when fewer than seven shelves exist.
                if (diagnosticTicks % 300 == 1)
                {
                    int stores = 0, initialized = 0, unpicked = 0;
                    foreach (Entity candidate in entities)
                    {
                        Item ci = manager.GetComponentData<Item>(candidate);
                        if (!ci.isStore) continue;
                        stores++;
                        if (ci.isInitialized) initialized++;
                        if (!ci.onPick) unpicked++;
                    }
                    string diagnostic = "[MagicraftAP] SHOP ECS | entities=" + entities.Length + " | stores=" + stores + " | initialized=" + initialized + " | unpicked=" + unpicked + " | eligible=" + shelves.Count;
                    if (diagnostic != lastDiagnostic) { log.LogWarning(diagnostic); lastDiagnostic = diagnostic; }
                }
                // Potion-room safety: NEVER construct a new visit from a mix of
                // previously-seen main-shop shelves and newly-spawned shelves.
                // The side room has six shelves; six new + one old used to be
                // incorrectly treated as a fresh seven-shelf main shop.
                // Require an EXACT seven-member room group, with ALL seven
                // physical entities unseen, before allowing a new AP visit.
                // Ambiguous groups are left vanilla (fail closed).
                if (shelves.Count >= 7)
                {
                    var groups = shelves.GroupBy(e =>
                    {
                        Item it = manager.GetComponentData<Item>(e);
                        return it.belongRoomMapPos.x + ":" + it.belongRoomMapPos.y;
                    }).ToList();
                    var candidates = groups
                        .Where(g => g.Count() == 7)
                        .Select(g => g.OrderBy(e => e.Index).ToList())
                        .Where(group => group.All(e => !seenShelfKeys.Contains(Key(manager, e))))
                        .ToList();
                    // When exactly seven shelves exist globally, allow the
                    // original v8.7 fallback, but only for seven NEW shelves.
                    // This retains support for builds with inconsistent room
                    // coordinates without admitting six potion shelves + old stock.
                    if (candidates.Count == 0 && shelves.Count == 7 &&
                        shelves.All(e => !seenShelfKeys.Contains(Key(manager, e))))
                        candidates.Add(shelves.OrderBy(e => e.Index).ToList());
                    if (candidates.Count == 1)
                    {
                        var current = candidates[0];
                        var keys = current.Select(e => Key(manager, e)).ToList();
                        string visit = string.Join("|", keys);
                        if (activeVisit != visit)
                        {
                            activeVisit = visit;
                            visitSequence++;
                            selectedShelves.Clear();
                            foreach (string k in keys) seenShelfKeys.Add(k);
                            var eligible = current.Where(e =>
                            {
                                Item it = manager.GetComponentData<Item>(e);
                                return it.info.type != ItemType.Relic ||
                                    !RelicConfig.dic.ContainsKey(it.info.id) ||
                                    RelicConfig.dic[it.info.id].dropType != ItemDropType.Epic;
                            }).ToList();
                            for (int i = eligible.Count - 1; i > 0; i--)
                            {
                                int j = UnityEngine.Random.Range(0, i + 1);
                                Entity temp = eligible[i]; eligible[i] = eligible[j]; eligible[j] = temp;
                            }
                            int take = Math.Min(Math.Min(client.MidRunShopApItems, eligible.Count),
                                locations.Count(id => !assigned.Contains(id) && !bought.Contains(id)));
                            foreach (Entity e in eligible.Take(take)) selectedShelves.Add(Key(manager, e));
                            log.LogInfo("[MagicraftAP] SHOP VISIT #" + visitSequence +
                                " | shelves=7 | eligible=" + eligible.Count +
                                " | requested=" + client.MidRunShopApItems +
                                " | AP selected=" + selectedShelves.Count +
                                " | selected=" + string.Join(",", selectedShelves.ToArray()));
                        }
                    }
                    else if (diagnosticTicks % 300 == 1)
                    {
                        log.LogInfo("[MagicraftAP] SHOP POTION SAFETY | no unambiguous NEW seven-shelf group" +
                            " | store shelves=" + shelves.Count +
                            " | groups=" + string.Join(",", groups.Select(g =>
                                g.Key + "=" + g.Count()).ToArray()) +
                            " | candidates=" + candidates.Count +
                            " | preserving existing assignments");
                    }
                }
                foreach (Entity entity in entities)
                {
                    Item item = manager.GetComponentData<Item>(entity);
                    if (!item.isStore || item.onPick || !item.isInitialized) continue;
                    // Epic relic shops use maximum HP, not coins. Leave them vanilla.
                    if (item.info.type == ItemType.Relic && RelicConfig.dic[item.info.id].dropType == ItemDropType.Epic) continue;
                    string key = Key(manager, entity);
                    // Only ever process a shelf explicitly selected from a
                    // verified seven-shelf visit; potion shelves are excluded.
                    if (!selectedShelves.Contains(key) && !stock.ContainsKey(key)) continue;
                    long location;
                    if (!stock.TryGetValue(key, out location))
                    {
                        if (!selectedShelves.Contains(key)) continue;
                        location = locations.FirstOrDefault(id => !assigned.Contains(id) && !bought.Contains(id));
                        if (location == 0) continue;
                        stock[key] = location; assigned.Add(location);
                        log.LogInfo("[MagicraftAP] AP NATIVE SHELF | " + key + " -> " + location);
                    }
                    if (bought.Contains(location)) continue;
                    ItemMono mono = item.itemMono.Value;
                    if (mono != null) { UpdateSign(key, mono, location); ApplyLogo(key, mono); }
                    InteractiveObj_Dots interaction = manager.GetComponentData<InteractiveObj_Dots>(entity);
                    if (!interaction.onInteract) continue;
                    // Read the underlying shelf price, including Magicraft discounts.
                    // Epic HP-priced relics are excluded above.
                    interaction.onInteract = false;
                    manager.SetComponentData(entity, interaction);
                    int price = item.GetFinalPrice();
                    if (price < 0) { log.LogWarning("[MagicraftAP] AP shop invalid price: " + price); continue; }
                    if (PlayerMgr.Inst.BaData.coinCount < price)
                    {
                        log.LogInfo("[MagicraftAP] AP SHOP: insufficient coins for " + location);
                        continue;
                    }
                    try
                    {
                        PlayerMgr.Inst.ChangeCoin(-price);
                        bought.Add(location);
                        client.SubmitShopPurchase(location);
                        item.onPick = true; // native ECS removes purchased shelf entity
                        manager.SetComponentData(entity, item);
                        purchaseCount++;
                        log.LogWarning("[MagicraftAP] AP NATIVE PURCHASE | " + location + " | " + Label(location) + " | " + price + " coins");
                        if (signs.ContainsKey(key) && signs[key] != null) UnityEngine.Object.Destroy(signs[key].gameObject);
                        signs.Remove(key);
                    }
                    catch (Exception ex) { log.LogError("[MagicraftAP] AP native purchase failed: " + ex); }
                }
            }
        }
        private static void ShopLog(string message)
        {
            if (log != null) log.LogWarning(message);
            else NativeShopItemSystemPatch.DiagnosticLog.LogWarning(message);
        }

        private static void Gate(string reason)
        {
            if (lastGate == reason) return;
            lastGate = reason;
            ShopLog("[MagicraftAP] SHOP GATE | " + reason);
        }
        private static void UpdateSign(string key, ItemMono mono, long location)
        {
            TextMesh text;
            if (!signs.TryGetValue(key, out text) || text == null)
            {
                var go = new GameObject("MagicraftAP_ShelfLabel");
                go.transform.SetParent(mono.transform, false);
                go.transform.localPosition = new Vector3(0f, 2.25f, -0.2f);
                text = go.AddComponent<TextMesh>();
                text.anchor = TextAnchor.UpperCenter;
                text.alignment = TextAlignment.Center;
                text.characterSize = 0.068f;
                text.fontSize = 28;
                text.GetComponent<MeshRenderer>().sortingOrder = 32000;
                signs[key] = text;
            }
            string label = Label(location);
            string itemName = label;
            string recipient = null;
            int arrow = label.LastIndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0) { itemName = label.Substring(0, arrow); recipient = label.Substring(arrow + 4); }
            if (itemName == "Archipelago Item" || itemName == "AP Item") itemName = "Unknown Archipelago Item";
            // Three bounded lines avoid text spilling into neighbouring shelves.
            // Underscores preserve the requested classification_slot_item format.
            string slot = string.IsNullOrEmpty(recipient) ? "UnknownSlot" : recipient;
            text.text = WrapShopText(Kind(location), 13) + "\n" +
                        WrapShopText(slot, 13) + "\n" +
                        WrapShopText(itemName, 13); // Native shelf handles the price.
        }
        private static string WrapShopText(string value, int width)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var parts = new List<string>();
            string remaining = value;
            while (remaining.Length > width)
            {
                int cut = remaining.LastIndexOf(' ', width);
                if (cut < width / 2) cut = width;
                parts.Add(remaining.Substring(0, cut).TrimEnd());
                remaining = remaining.Substring(cut).TrimStart();
            }
            if (remaining.Length > 0) parts.Add(remaining);
            return string.Join("\n", parts.ToArray());
        }

        // ScoutedItemInfo.Player is an SDK object, not necessarily a slot number.
        // Inspect its actual type before attempting to resolve it through session.Players.
        internal static string ResolveRecipient(ArchipelagoSession session, object item)
        {
            object receiver = MemberValue(item, "Player");
            if (receiver == null) return "UnknownSlot";

            // Some SDK versions return a PlayerInfo with Name/Alias and Slot.
            string directName = FirstName(receiver);
            if (!string.IsNullOrWhiteSpace(directName)) return directName;
            if (receiver is string receiverName && !int.TryParse(receiverName, out _))
                return receiverName;

            int slot;
            if (!int.TryParse(Convert.ToString(receiver), out slot))
            {
                object slotValue = MemberValue(receiver, "Slot") ?? MemberValue(receiver, "Player")
                    ?? MemberValue(receiver, "Id") ?? MemberValue(receiver, "SlotId")
                    ?? MemberValue(receiver, "PlayerId");
                if (slotValue == null || !int.TryParse(Convert.ToString(slotValue), out slot))
                    return "UnknownSlot";
            }

            try
            {
                object players = session.Players;
                if (players == null) return "Slot" + slot;
                // The Archipelago SDK commonly exposes GetPlayerInfo(slot) and/or
                // GetPlayerName(slot); avoid assuming a particular SDK version.
                foreach (string methodName in new[] { "GetPlayerName", "GetPlayerAlias", "GetPlayerInfo" })
                {
                    foreach (MethodInfo method in players.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m => m.Name == methodName))
                    {
                        ParameterInfo[] parameters = method.GetParameters();
                        if (parameters.Length != 1) continue;
                        Type type = parameters[0].ParameterType;
                        if (type != typeof(int) && type != typeof(long) && type != typeof(short)) continue;
                        try
                        {
                            object resolved = method.Invoke(players, new[] { Convert.ChangeType(slot, type) });
                            string name = FirstName(resolved);
                            if (!string.IsNullOrWhiteSpace(name)) return name;
                            if (resolved is string resultName && !string.IsNullOrWhiteSpace(resultName))
                                return resultName;
                        }
                        catch { /* Try other supported player directory methods. */ }
                    }
                }
            }
            catch (Exception e) { loggerRecipientWarning = e.Message; }
            return "Slot" + slot;
        }

        private static string FirstName(object value)
        {
            if (value == null) return null;
            foreach (string field in new[] { "Name", "Alias", "SlotName", "PlayerName" })
            {
                object name = MemberValue(value, field);
                if (name != null && !string.IsNullOrWhiteSpace(Convert.ToString(name)))
                    return Convert.ToString(name);
            }
            return null;
        }

        private static object MemberValue(object obj, string name)
        {
            if (obj == null) return null;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            var property = obj.GetType().GetProperty(name, flags);
            if (property != null) return property.GetValue(obj, null);
            var field = obj.GetType().GetField(name, flags);
            return field == null ? null : field.GetValue(obj);
        }

        private static string loggerRecipientWarning;
        // Load the genuine Archipelago logo supplied by the user. Do not replace
        // it with a guessed lookalike. Copy APLogo.png beside the plugin DLL.
        private static Texture2D LoadLogo()
        {
            if (logoAttempted) return apLogo;
            logoAttempted = true;
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(typeof(ArchipelagoShop).Assembly.Location), "APLogo.png");
                if (!File.Exists(path))
                {
                    log.LogWarning("[MagicraftAP] APLogo.png missing next to plugin DLL; keeping vanilla icon until supplied.");
                    return null;
                }
                byte[] bytes = File.ReadAllBytes(path);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes))
                {
                    UnityEngine.Object.Destroy(texture);
                    log.LogWarning("[MagicraftAP] APLogo.png could not be decoded.");
                    return null;
                }
                texture.name = "MagicraftAP_Logo";
                texture.wrapMode = TextureWrapMode.Clamp;
                apLogo = texture;
                log.LogInfo("[MagicraftAP] AP shelf logo loaded: " + path);
            }
            catch (Exception e) { log.LogWarning("[MagicraftAP] AP logo load failed: " + e); }
            return apLogo;
        }
        private static void ApplyLogo(string key, ItemMono mono)
        {
            Texture2D texture = LoadLogo();
            if (texture == null || mono == null || mono.mrIcon == null) return;
            if (iconApplied.Contains(key)) return;
            mono.mrIcon.gameObject.SetActive(true);
            mono.mrIcon.material.SetTexture("_MainTex", texture);
            if (mono.mrOutline != null) mono.mrOutline.material.SetTexture("_MainTex", texture);
            iconApplied.Add(key);
        }
        // Vanilla refresh can reset textures, so reapply after UpdateDisplay.
        public static void RefreshLogo(ItemMono mono)
        {
            if (mono == null || client == null || !client.IsConnected) return;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var manager = world.EntityManager;
            if (!manager.Exists(mono.itemEtt) || !manager.HasComponent<Item>(mono.itemEtt)) return;
            string key = Key(manager, mono.itemEtt);
            if (!stock.ContainsKey(key)) return;
            iconApplied.Remove(key);
            ApplyLogo(key, mono);
        }
        // SDK 6.7.1 scouting remains best-effort. Item recipient/name is
        // provided by the AP server, not invented by Magicraft.
        public static void Scout(ArchipelagoSession session, ManualLogSource logger)
        {
            if (session == null) return;
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    long[] ids = session.Locations.AllLocations.ToArray();
                    foreach (MethodInfo method in session.Locations.GetType().GetMethods().Where(m => m.Name == "ScoutLocationsAsync"))
                    {
                        try
                        {
                            var pars = method.GetParameters(); object[] args = new object[pars.Length]; bool ok = false, valid = true;
                            for (int i = 0; i < pars.Length; i++)
                            {
                                Type t = pars[i].ParameterType;
                                if (t == typeof(long[]) || typeof(IEnumerable<long>).IsAssignableFrom(t)) { args[i] = ids; ok = true; }
                                else if (t.IsEnum) args[i] = Enum.ToObject(t, 0);
                                else if (pars[i].HasDefaultValue) args[i] = pars[i].DefaultValue;
                                else { valid = false; break; }
                            }
                            if (!valid || !ok) continue;
                            var task = method.Invoke(session.Locations, args) as System.Threading.Tasks.Task;
                            if (task == null) continue;
                            await task.ConfigureAwait(false);
                            object result = task.GetType().GetProperty("Result")?.GetValue(task, null);
                            var map = result as System.Collections.IDictionary;
                            if (map != null)
                                foreach (System.Collections.DictionaryEntry entry in map)
                                {
                                    long id = Convert.ToInt64(entry.Key);
                                    object value = entry.Value;
                                    string name = Property(value, "ItemName") ?? Property(value, "Name") ?? "AP Item";
                                    string recipient = ResolveRecipient(session, value);
                                    if (recipient == "UnknownSlot")
                                    {
                                        logger.LogWarning("[MagicraftAP] SCOUT RECIPIENT UNKNOWN | location=" + id +
                                            " | playerType=" + (MemberValue(value, "Player") == null ? "null" : MemberValue(value, "Player").GetType().FullName) +
                                            " | playerValue=" + Convert.ToString(MemberValue(value, "Player")));
                                    }
                                    if (id >= 273905001 && id <= 273905060) SetScoutedLabel(id, name + " -> " + recipient);
                                    string flags = Property(value, "Flags") ?? Property(value, "ItemFlags") ?? "0";
                                    long bits = 0;
                                    if (!long.TryParse(flags, out bits))
                                    {
                                        if (flags.IndexOf("Advancement", StringComparison.OrdinalIgnoreCase) >= 0 || flags.IndexOf("Progression", StringComparison.OrdinalIgnoreCase) >= 0) bits |= 1;
                                        if (flags.IndexOf("Useful", StringComparison.OrdinalIgnoreCase) >= 0) bits |= 2;
                                        if (flags.IndexOf("Trap", StringComparison.OrdinalIgnoreCase) >= 0) bits |= 4;
                                    }
                                    string kind = (bits & 1) != 0 ? "progression" : (bits & 4) != 0 ? "trap" : (bits & 2) != 0 ? "useful" : "filler";
                                    lock (labelLock) if (id >= 273905001 && id <= 273905060) itemKinds[id] = kind;
                                    ArchipelagoNotifications.SetScouted(id, name, recipient, kind);
                                }
                            if (loggerRecipientWarning != null) logger.LogWarning("[MagicraftAP] Recipient lookup: " + loggerRecipientWarning);
                            logger.LogInfo("[MagicraftAP] Native shop scouting completed.");
                            return;
                        }
                        catch (Exception e) { logger.LogWarning("[MagicraftAP] Scout overload: " + e.Message); }
                    }
                }
                catch (Exception e) { logger.LogWarning("[MagicraftAP] Native shop scouting failed: " + e); }
            });
        }
        private static string Property(object obj, string name)
        {
            if (obj == null) return null;
            object val = null;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            var prop = obj.GetType().GetProperty(name, flags);
            if (prop != null) val = prop.GetValue(obj, null);
            if (val == null)
            {
                var field = obj.GetType().GetField(name, flags);
                if (field != null) val = field.GetValue(obj);
            }
            return val == null ? null : val.ToString();
        }
    }

    [HarmonyPatch]
    public static class NativeShopItemSystemPatch
    {
        // HarmonyTargetMethod on Target() was already valid and registered.
        // Use the conventional TargetMethod name for clarity.
        [HarmonyTargetMethod]
        private static MethodBase TargetMethod() { return AccessTools.Method(typeof(ItemSystem), "OnUpdate"); }

        internal static readonly ManualLogSource DiagnosticLog =
            BepInEx.Logging.Logger.CreateLogSource("MagicraftAP.NativeShop");
        private static bool prefixLogged;
        [HarmonyPrefix]
        private static void Prefix()
        {
            if (!prefixLogged)
            {
                prefixLogged = true;
                DiagnosticLog.LogWarning("[MagicraftAP] SHOP PREFIX EXECUTING | ItemSystem.OnUpdate");
            }
            try { ArchipelagoShop.BeforeNativeItemUpdate(); }
            catch (Exception e) { DiagnosticLog.LogError("[MagicraftAP] SHOP PREFIX ERROR | " + e); }
        }
    }
}

namespace MagicraftAP
{
    [HarmonyPatch(typeof(ItemMono), "UpdateDisplay")]
    public static class NativeShopLogoRefreshPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ItemMono __instance)
        {
            try { ArchipelagoShop.RefreshLogo(__instance); }
            catch (Exception e) { NativeShopItemSystemPatch.DiagnosticLog.LogWarning("[MagicraftAP] AP logo refresh: " + e.Message); }
        }
    }
}
