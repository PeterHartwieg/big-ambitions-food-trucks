using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace BridgeControl
{
    // A coordination lock for trusted local tools, not an OS security boundary. Ownership does not expire.
    // Every state transition uses the same cross-process file gate. A crashed process releases the gate;
    // its work reservations are pruned by PID + creation time, but its ownership token remains required.
    public sealed class LeaseStore
    {
        private readonly string directory, instance, legacy;
        public LeaseStore(string directory, string instance, string legacy = null)
        { this.directory = directory; this.instance = instance; this.legacy = legacy; }

        public static LeaseStore ForInstance(string instance)
        {
            if (instance != "normal" && instance != "agent1" && instance != "agentmac") throw new InvalidOperationException("Unknown bridge instance");
            var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new LeaseStore(Path.Combine(user, ".big-ambitions-bridge", instance), instance,
                instance == "normal" ? Path.Combine(user, "game-lock.txt") : null);
        }

        [DllImport("libc", SetLastError = true)] private static extern int flock(int descriptor, int operation);
        private static readonly bool Unix = Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX;
        private FileStream OpenGate()
        {
            // Mono and .NET implement FileShare differently on Unix. Use the same kernel flock in both.
            var file = new FileStream(Path.Combine(directory, "gate"), FileMode.OpenOrCreate, FileAccess.ReadWrite,
                Unix ? FileShare.ReadWrite : FileShare.None);
            if (Unix && flock(file.SafeFileHandle.DangerousGetHandle().ToInt32(), 2 | 4) != 0)
            { file.Dispose(); throw new IOException("The lock state is busy"); }
            return file;
        }

        private T Edit<T>(Func<JObject, T> action)
        {
            Directory.CreateDirectory(directory);
            FileStream gate = null;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                try { gate = OpenGate(); break; }
                catch (IOException) { if (attempt == 99) throw; Thread.Sleep(10); }
            }
            using (gate)
            {
                var path = Path.Combine(directory, "lease.json");
                var state = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject { ["version"] = 1, ["instance"] = instance, ["pending"] = new JArray() };
                if ((int?)state["version"] != 1 || (string)state["instance"] != instance || !(state["pending"] is JArray))
                    throw new InvalidOperationException("Unrecognized lock state; refusing to change it");
                var original = state.ToString();
                foreach (var item in ((JArray)state["pending"]).ToArray())
                    if (!Alive((int)item["pid"], (long)item["started_ticks"])) item.Remove();
                T result = action(state);
                if (File.Exists(path) && original == state.ToString()) return result;
                var temp = path + "." + Guid.NewGuid().ToString("N");
                try
                {
                    using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var bytes = Encoding.UTF8.GetBytes(state.ToString()); stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                    }
                    if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                return result;
            }
        }

        private static bool Alive(int pid, long ticks)
        {
            try { using (var p = Process.GetProcessById(pid)) return p.StartTime.ToUniversalTime().Ticks == ticks; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch { return true; } // Cannot prove it ended: preserve the reservation.
        }

        private void CheckLegacy()
        {
            if (legacy != null && File.Exists(legacy))
                throw new InvalidOperationException("The normal game has a legacy game-lock.txt reservation. Its holder must release it first; it is never overridden automatically.");
        }

        private static string Hash(string token)
        {
            if (token == null || token.Length < 32 || token.Length > 256) throw new InvalidOperationException("A valid lock token is required");
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(token))).Replace("-", "").ToLowerInvariant();
        }

        private void Require(JObject state, string token)
        {
            CheckLegacy();
            if (state["token_sha256"] == null) throw new InvalidOperationException("Acquire the " + instance + " lock before changing the game");
            if ((string)state["token_sha256"] != Hash(token))
                throw new InvalidOperationException("The " + instance + " lock belongs to " + (string)state["owner"] + "; this token cannot control it");
        }

        private JObject Public(JObject state) => new JObject
        {
            ["instance"] = instance, ["held"] = state["token_sha256"] != null,
            ["owner"] = state["owner"]?.DeepClone(), ["purpose"] = state["purpose"]?.DeepClone(),
            ["acquired_utc"] = state["acquired_utc"]?.DeepClone(), ["expires_automatically"] = false,
            ["pending"] = ((JArray)state["pending"]).Count,
            ["legacy_blocked"] = legacy != null && File.Exists(legacy)
        };

        public JObject Status() => Edit(Public);
        public string StatusJson() => Status().ToString();
        public JObject Acquire(string owner, string purpose, string token) => Edit(state =>
        {
            CheckLegacy();
            if (string.IsNullOrWhiteSpace(owner) || owner.Length > 120 || owner.Any(char.IsControl)) throw new InvalidOperationException("Use a short, explicit lock owner name");
            if (purpose != null && purpose.Length > 500) throw new InvalidOperationException("Lock purpose is too long");
            var hash = Hash(token);
            if (state["token_sha256"] != null)
            {
                Require(state, token);
                if ((string)state["owner"] != owner) throw new InvalidOperationException("The lock owner name does not match this token");
                return Public(state); // Idempotent retry after an unknown reply.
            }
            state["owner"] = owner; state["purpose"] = purpose; state["token_sha256"] = hash;
            state["acquired_utc"] = DateTime.UtcNow.ToString("o");
            return Public(state);
        });
        public string AcquireJson(string owner, string purpose, string token) => Acquire(owner, purpose, token).ToString();

        public JObject Release(string token) => Edit(state =>
        {
            Require(state, token);
            if (((JArray)state["pending"]).Count != 0) throw new InvalidOperationException("Commands or lifecycle work are still pending; wait for completion before releasing the lock");
            foreach (var key in new[] { "owner", "purpose", "token_sha256", "acquired_utc" }) state.Remove(key);
            return Public(state);
        });
        public string ReleaseJson(string token) => Release(token).ToString();
        public void AssertOwner(string token) => Edit(state => { Require(state, token); return 0; });

        public void AssertPermit(string token, string id) => Edit(state =>
        {
            Require(state, token);
            if (!((JArray)state["pending"]).Any(p => (string)p["id"] == id && (string)p["kind"] == "lifecycle"))
                throw new InvalidOperationException("The launch reservation is no longer active");
            return 0;
        });

        public string Begin(string token, string kind) => Edit(state =>
        {
            Require(state, token);
            var pending = (JArray)state["pending"];
            if (pending.Any(p => (string)p["kind"] == "lifecycle") || (kind == "lifecycle" && pending.Count != 0))
                throw new InvalidOperationException("Another operation is pending; lifecycle changes require an idle instance");
            var id = Guid.NewGuid().ToString("N");
            using (var process = Process.GetCurrentProcess())
                pending.Add(new JObject { ["id"] = id, ["pid"] = process.Id, ["started_ticks"] = process.StartTime.ToUniversalTime().Ticks, ["kind"] = kind == "terminate" ? "lifecycle" : kind });
            return id;
        });
        public void End(string id) => Edit(state =>
        {
            foreach (var p in ((JArray)state["pending"]).Where(p => (string)p["id"] == id).ToArray()) p.Remove();
            return 0;
        });
    }
}
