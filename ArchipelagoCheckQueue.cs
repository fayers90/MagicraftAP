using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Logging;

namespace MagicraftAP
{
    // Only a successfully authenticated session can establish a queue identity.
    // A check made before first login is deliberately not queued.
    public sealed class ArchipelagoCheckQueue
    {
        private readonly ManualLogSource log;
        private readonly object gate = new object();
        private string identity;
        private string path;
        private readonly HashSet<long> pending = new HashSet<long>();

        public ArchipelagoCheckQueue(ManualLogSource logger) { log = logger; }

        public void Bind(string seed, string slot)
        {
            if (string.IsNullOrWhiteSpace(seed) || string.IsNullOrWhiteSpace(slot))
                throw new InvalidOperationException("Cannot bind AP queue without verified seed and slot.");
            string key = seed + "\n" + slot;
            string file = Path.Combine(SaveRedirectPatch.ProfilePath, "ModData", "PendingChecks_" + Hash(key) + ".txt");
            lock (gate)
            {
                // Never carry checks across identities, even within the same game process.
                pending.Clear();
                identity = null;
                path = null;
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                if (File.Exists(file))
                {
                    foreach (string line in File.ReadAllLines(file))
                    {
                        long id;
                        if (long.TryParse(line.Trim(), out id)) pending.Add(id);
                    }
                }
                path = file;
                identity = key;
                log.LogWarning("[MagicraftAP] PENDING QUEUE BOUND | Slot: " + slot + " | Existing: " + pending.Count);
            }
        }

        public void Unbind()
        {
            lock (gate) { identity = null; path = null; pending.Clear(); }
        }

        public bool IsBound { get { lock (gate) return identity != null; } }

        public bool Enqueue(long id)
        {
            lock (gate)
            {
                if (identity == null) return false;
                if (pending.Contains(id)) return true;
                pending.Add(id);
                try { Save(); return true; }
                catch (Exception ex)
                {
                    pending.Remove(id);
                    log.LogError("[MagicraftAP] Pending check not persisted: " + ex);
                    return false;
                }
            }
        }

        public long[] Snapshot() { lock (gate) return pending.OrderBy(x => x).ToArray(); }

        public void Remove(long id)
        {
            lock (gate)
            {
                if (!pending.Remove(id)) return;
                try { Save(); }
                catch (Exception ex) { pending.Add(id); log.LogError("[MagicraftAP] Pending queue save failed: " + ex); }
            }
        }

        private void Save()
        {
            if (path == null) throw new InvalidOperationException("Queue not bound.");
            // Write temp then replace; never overwrite another seed's file.
            string temp = path + ".tmp";
            File.WriteAllLines(temp, pending.OrderBy(x => x).Select(x => x.ToString()).ToArray());
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        private static string Hash(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes("MagicraftAP Queue\n" + value));
                var sb = new StringBuilder();
                for (int i = 0; i < 16; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
