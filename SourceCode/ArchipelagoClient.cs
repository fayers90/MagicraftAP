using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using BepInEx.Logging;

namespace MagicraftAP
{
    public class ArchipelagoClient
    {
        private const string GameName = "Magicraft";
        private const long ChestBaseId = 273901000L;
        private readonly ManualLogSource log;
        private readonly object stateLock = new object();
        private ArchipelagoSession session;
        private string status = "Disconnected";
        private bool connecting;
        private bool connected;
        private int connectionGeneration;
        private readonly HashSet<long> submittedChestIds = new HashSet<long>();
        private readonly ArchipelagoCheckQueue pending;
        private string verifiedSeed;
        private string verifiedSlot;
        private readonly Dictionary<string, long> locationIdCache = new Dictionary<string, long>(StringComparer.Ordinal);
        private long[] knownChestIds = new long[0];
        private readonly HashSet<long> knownCheckedChests = new HashSet<long>();
        private bool hasEverAuthenticated;
        private object currentLogin;
        private int midRunShopApItems = 4;
        public int MidRunShopApItems { get { lock (stateLock) return midRunShopApItems; } }
        private static int ReadShopDensity(LoginResult login)
        {
            // AP login success carries the generated per-player slot_data.
            // Reflection avoids coupling to a particular SDK slot-data dictionary type.
            try
            {
                object data = login.GetType().GetProperty("SlotData")?.GetValue(login, null);
                if (data is System.Collections.IDictionary dict)
                {
                    foreach (System.Collections.DictionaryEntry entry in dict)
                        if (string.Equals(Convert.ToString(entry.Key), "mid_run_shop_ap_items", StringComparison.OrdinalIgnoreCase))
                            return Math.Max(0, Math.Min(7, Convert.ToInt32(entry.Value)));
                }
            }
            catch { }
            return 4; // Backwards compatibility with seeds predating this option.
        }

        // Explicit IDs currently defined in the existing .apworld.
        private static readonly Dictionary<int, long> NpcLocationIds = new Dictionary<int, long>
        {
            { 3, 273902003L }, { 4, 273902004L }, { 5, 273902005L },
            { 6, 273902006L }, { 7, 273902007L }, { 9, 273902009L }
        };

        public ArchipelagoClient(ManualLogSource logger) { log = logger; pending = new ArchipelagoCheckQueue(logger); }
        public string Status { get { lock (stateLock) return status; } }
        public bool IsConnected { get { lock (stateLock) return connected; } }

        public void Connect(string host, int port, string slotName, string password)
        {
            host = (host ?? "").Trim();
            slotName = (slotName ?? "").Trim();
            if (host.Length == 0 || slotName.Length == 0 || port < 1 || port > 65535)
            {
                lock (stateLock) status = "Invalid connection details";
                return;
            }
            int generation;
            lock (stateLock)
            {
                if (connecting || connected) return;
                connecting = true;
                connected = false;
                status = "Connecting...";
                generation = ++connectionGeneration;
                submittedChestIds.Clear();
                ArchipelagoItemState.Reset();
                locationIdCache.Clear();
                verifiedSeed = null;
                verifiedSlot = null;
                hasEverAuthenticated = false;
                midRunShopApItems = 4;
                knownChestIds = new long[0];
                knownCheckedChests.Clear();
            }
            log.LogInfo("[MagicraftAP] Connecting to " + host + ":" + port + " as " + slotName);
            Task.Run(() =>
            {
                ArchipelagoSession newSession = null;
                try
                {
                    newSession = ArchipelagoSessionFactory.CreateSession(host, port);
                    LoginResult result = newSession.TryConnectAndLogin(
                        GameName, slotName, ItemsHandlingFlags.AllItems,
                        password: string.IsNullOrEmpty(password) ? null : password);
                    if (!result.Successful)
                    {
                        LoginFailure failure = result as LoginFailure;
                        string reason = failure == null ? "Login rejected" :
                            string.Join("; ", failure.Errors.Select(x => x.ToString())
                                .Concat(failure.ErrorCodes.Select(x => x.ToString())).ToArray());
                        lock (stateLock)
                        {
                            if (generation == connectionGeneration)
                            {
                                connecting = false;
                                connected = false;
                                status = "Failed: " + reason;
                            }
                        }
                        log.LogWarning("[MagicraftAP] AP login failed: " + reason);
                        newSession.Socket.DisconnectAsync().GetAwaiter().GetResult();
                        return;
                    }
                    // RoomState.Seed is supplied by the authenticated server.
                    // Refuse a session if identity cannot be established.
                    string seed = ReadSeed(newSession);
                    pending.Bind(seed, slotName);
                    ArchipelagoItemState.Configure(result, log);
                    bool accepted;
                    lock (stateLock)
                    {
                        accepted = generation == connectionGeneration;
                        if (accepted)
                        {
                            session = newSession;
                            connecting = false;
                            connected = true;
                            status = "Connected as " + slotName;
                            verifiedSeed = seed;
                            verifiedSlot = slotName;
                            hasEverAuthenticated = true;
                            currentLogin = result;
                            midRunShopApItems = ReadShopDensity(result);
                        }
                    }
                    if (!accepted)
                    {
                        newSession.Socket.DisconnectAsync().GetAwaiter().GetResult();
                        return;
                    }
                    log.LogInfo("[MagicraftAP] ARCHIPELAGO CONNECTED | " + host + ":" + port + " | Slot: " + slotName + " | Seed: " + seed);
                    BuildLocationCache(newSession);
                    log.LogInfo("[MagicraftAP] Mid-run AP shelves per shop: " + MidRunShopApItems);
                    ArchipelagoShop.ClearLabels();
                    ArchipelagoNotifications.BeginSession(newSession);
                    ArchipelagoShop.Scout(newSession, log);
                    lock (stateLock)
                    {
                        knownChestIds = newSession.Locations.AllLocations
                            .Where(id => id > ChestBaseId && id <= ChestBaseId + 150)
                            .OrderBy(id => id).ToArray();
                        knownCheckedChests.Clear();
                        foreach (long id in newSession.Locations.AllLocationsChecked)
                            if (id > ChestBaseId && id <= ChestBaseId + 150) knownCheckedChests.Add(id);
                    }
                    PollItems();
                    FlushPending();
                    try
                    {
                        int available = newSession.Locations.AllLocations.Count(id => id > ChestBaseId && id <= ChestBaseId + 150);
                        int checkedCount = newSession.Locations.AllLocationsChecked.Count(id => id > ChestBaseId && id <= ChestBaseId + 150);
                        log.LogInfo("[MagicraftAP] AP CHESTS | Available: " + available + " | Server checked: " + checkedCount);
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning("[MagicraftAP] AP chest state query failed: " + ex);
                    }
                }
                catch (Exception ex)
                {
                    lock (stateLock)
                    {
                        if (generation == connectionGeneration)
                        {
                            connecting = false;
                            connected = false;
                            session = null;
                            status = "Connection error: " + ex.GetBaseException().Message;
                        }
                    }
                    log.LogError("[MagicraftAP] AP connection error: " + ex);
                    try { if (newSession != null) newSession.Socket.DisconnectAsync().GetAwaiter().GetResult(); }
                    catch { }
                }
            });
        }

        // Called by the chest detector on Unity's main thread.
        // The server's checked-location list is authoritative; local history is ignored.
        public void SubmitNextChestCheck()
        {
            long chosen = 0;
            lock (stateLock)
            {
                if (!hasEverAuthenticated || !pending.IsBound || knownChestIds.Length == 0)
                {
                    log.LogWarning("[MagicraftAP] CHEST NOT QUEUED: connect to an AP seed before playing.");
                    return;
                }
                HashSet<long> queued = new HashSet<long>(pending.Snapshot());
                foreach (long id in knownChestIds)
                {
                    if (!knownCheckedChests.Contains(id) && !submittedChestIds.Contains(id) && !queued.Contains(id))
                    {
                        chosen = id;
                        submittedChestIds.Add(id);
                        break;
                    }
                }
            }
            if (chosen == 0)
            {
                log.LogInfo("[MagicraftAP] No unchecked AP chest locations remaining.");
                return;
            }
            SubmitLocation(chosen, "Chest Opened " + (chosen - ChestBaseId).ToString("000"));
        }

        private static string ReadSeed(ArchipelagoSession active)
        {
            PropertyInfo roomProperty = active.GetType().GetProperty("RoomState");
            object room = roomProperty == null ? null : roomProperty.GetValue(active, null);
            PropertyInfo seedProperty = room == null ? null : room.GetType().GetProperty("Seed");
            object seed = seedProperty == null ? null : seedProperty.GetValue(room, null);
            if (seed == null || string.IsNullOrWhiteSpace(seed.ToString()))
                throw new InvalidOperationException("AP seed identity unavailable; refusing unsafe queue binding.");
            return seed.ToString();
        }

        // These calls run on Unity's main thread. Unknown .apworld locations
        // are NOT queued: no ID can safely be assigned until generation defines one.
        // Shop locations are owned by the AP server, not by local saves.
        public long[] GetShopLocations()
        {
            lock (stateLock)
            {
                if (!connected || session == null) return new long[0];
                return session.Locations.AllLocations
                    .Where(id => id >= 273905001L && id <= 273905060L &&
                        !session.Locations.AllLocationsChecked.Contains(id))
                    .OrderBy(id => id).ToArray();
            }
        }

        // Call only after a VERIFIED successful shop purchase; the seed-bound
        // pending queue and server authority are handled by SubmitLocation.
        public void SubmitShopPurchase(long locationId)
        {
            if (locationId < 273905001L || locationId > 273905060L) return;
            SubmitLocation(locationId, "Shop Purchase " + (locationId - 273905000L).ToString("000"));
        }

        public void SubmitNpcUnlock(int npcId)
        {
            long id;
            if (NpcLocationIds.TryGetValue(npcId, out id)) SubmitLocation(id, "NPC" + npcId);
        }

        public void SubmitRoomClear(string locationName)
        {
            SubmitNamedLocation(locationName);
        }

        public void SubmitEliteClear(string locationName)
        {
            SubmitNamedLocation(locationName);
        }

        private void BuildLocationCache(ArchipelagoSession current)
        {
            try
            {
                var methods = current.Locations.GetType().GetMethods();
                MethodInfo nameMethod = methods.FirstOrDefault(m => m.Name == "GetLocationNameFromId" &&
                    m.GetParameters().Length >= 1 && m.GetParameters()[0].ParameterType == typeof(long));
                if (nameMethod == null) { log.LogWarning("[MagicraftAP] AP location name cache unavailable."); return; }
                var map = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (long id in current.Locations.AllLocations)
                {
                    var pars = nameMethod.GetParameters();
                    object[] args = new object[pars.Length];
                    args[0] = id;
                    for (int i = 1; i < pars.Length; i++)
                        args[i] = pars[i].ParameterType == typeof(string) ? GameName :
                            (pars[i].HasDefaultValue ? pars[i].DefaultValue : null);
                    string name = nameMethod.Invoke(current.Locations, args) as string;
                    if (!string.IsNullOrWhiteSpace(name)) map[name] = id;
                }
                lock (stateLock)
                {
                    if (ReferenceEquals(session, current))
                    {
                        locationIdCache.Clear();
                        foreach (var pair in map) locationIdCache[pair.Key] = pair.Value;
                    }
                }
                log.LogInfo("[MagicraftAP] AP location cache loaded: " + map.Count);
            }
            catch (Exception ex) { log.LogWarning("[MagicraftAP] Location cache error: " + ex.Message); }
        }

        private void SubmitNamedLocation(string name)
        {
            long id;
            lock (stateLock)
            {
                if (locationIdCache.TryGetValue(name, out id))
                {
                    SubmitLocation(id, name);
                    return;
                }
            }
            log.LogWarning("[MagicraftAP] CHECK NOT QUEUED (location not defined in authenticated seed): " + name);
        }

        public void SubmitLocation(long id, string name)
        {
            ArchipelagoSession current;
            bool authenticated;
            lock (stateLock)
            {
                current = session;
                authenticated = hasEverAuthenticated && verifiedSeed != null && pending.IsBound;
            }
            if (!authenticated)
            {
                log.LogWarning("[MagicraftAP] CHECK NOT QUEUED (seed not authenticated): " + name);
                return;
            }
            try
            {
                if (current != null && current.Socket.Connected &&
                    current.Locations.AllLocationsChecked.Contains(id))
                {
                    pending.Remove(id);
                    return;
                }
                if (current != null && current.Socket.Connected &&
                    !current.Locations.AllLocations.Contains(id))
                {
                    log.LogInfo("[MagicraftAP] AP location absent from this seed: " + name + " (" + id + ")");
                    return;
                }
                if (!pending.Enqueue(id)) return;
                log.LogWarning("[MagicraftAP] CHECK QUEUED | " + name + " | ID: " + id);
                FlushPending();
            }
            catch (Exception ex) { log.LogError("[MagicraftAP] Check queue error: " + ex); }
        }

        public void FlushPending()
        {
            ArchipelagoSession current;
            string seed, slot;
            lock (stateLock) { current = session; seed = verifiedSeed; slot = verifiedSlot; }
            if (current == null || !current.Socket.Connected || !pending.IsBound) return;
            try
            {
                if (ReadSeed(current) != seed || string.IsNullOrWhiteSpace(slot))
                {
                    log.LogError("[MagicraftAP] QUEUE FLUSH BLOCKED: seed/slot identity mismatch.");
                    return;
                }
                foreach (long id in pending.Snapshot())
                {
                    if (current.Locations.AllLocationsChecked.Contains(id))
                    {
                        pending.Remove(id);
                        lock (stateLock) if (id > ChestBaseId && id <= ChestBaseId + 150) knownCheckedChests.Add(id);
                        continue;
                    }
                    if (!current.Locations.AllLocations.Contains(id))
                    {
                        log.LogWarning("[MagicraftAP] Pending ID not present in connected seed; retaining: " + id);
                        continue;
                    }
                    current.Locations.CompleteLocationChecks(id);
                    // Retain until server confirms check; safe to resend on next connection.
                    log.LogWarning("[MagicraftAP] PENDING CHECK SENT | ID: " + id + " | Awaiting server confirmation");
                }
            }
            catch (Exception ex) { log.LogError("[MagicraftAP] Pending flush failed: " + ex); }
        }

        public void PollItems()
        {
            ArchipelagoSession current;
            lock (stateLock) current = connected ? session : null;
            if (current != null && current.Socket.Connected)
                {
                    ArchipelagoItemState.Refresh(current, log);
                    ArchipelagoNotifications.Poll(current);
                }
        }

        public void Disconnect()
        {
            ArchipelagoSession oldSession;
            lock (stateLock)
            {
                ++connectionGeneration;
                oldSession = session;
                session = null;
                connecting = false;
                connected = false;
                status = "Disconnected";
                submittedChestIds.Clear();
                verifiedSeed = null;
                verifiedSlot = null;
                locationIdCache.Clear();
                knownChestIds = new long[0];
                knownCheckedChests.Clear();
                hasEverAuthenticated = false;
                currentLogin = null;
                ArchipelagoItemState.Reset();
                ArchipelagoNotifications.EndSession();
                pending.Unbind();
            }
            if (oldSession != null)
                Task.Run(() =>
                {
                    try { oldSession.Socket.DisconnectAsync().GetAwaiter().GetResult(); }
                    catch (Exception ex) { log.LogWarning("[MagicraftAP] Disconnect error: " + ex.Message); }
                });
            log.LogInfo("[MagicraftAP] AP disconnected.");
        }

        public void RefreshConnectionStatus()
        {
            ArchipelagoSession current;
            lock (stateLock)
            {
                if (!connected || session == null) return;
                current = session;
            }
            if (!current.Socket.Connected)
            {
                lock (stateLock)
                {
                    if (ReferenceEquals(session, current))
                    {
                        connected = false;
                        connecting = false;
                        session = null;
                        submittedChestIds.Clear();
                        status = "Connection lost";
                    }
                }
                log.LogWarning("[MagicraftAP] AP connection lost.");
            }
        }
    }
}
