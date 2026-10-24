#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UI.Notification;

namespace FoodTrucks
{
    // The bridge's HTTP side: a loopback-only HttpListener on a background thread, each request served on a pool thread,
    // so /health, /events and op queries answer while a /call waits for the main thread. It never touches the game:
    // /health and /events read what the main thread left for them, /call waits for the main thread, where calls run one
    // after another.
    internal sealed class TestBridgeHttp
    {
        private readonly int port;
        // The bridge generation this server was started under, fixed for its life: a request it accepts is refused at the
        // main thread's queue once that generation has ended, even if a stop and a start came in between.
        private readonly int generation;
        private HttpListener? listener;
        private Thread? thread;
        private volatile bool running;

        public TestBridgeHttp(int port, int generation)
        {
            this.port = port;
            this.generation = generation;
        }

        public void Start()
        {
            var l = new HttpListener();
            l.Prefixes.Add($"http://127.0.0.1:{port}/");
            l.Start();
            listener = l;
            running = true;
            thread = new Thread(Loop) { Name = "FoodTrucks.TestBridge.Http", IsBackground = true };
            thread.Start();
        }

        public void Stop()
        {
            running = false;
            try
            {
                listener?.Stop();
                listener?.Close();
            }
            catch (Exception) { /* Shutting down anyway. */ }
            thread?.Join(1000);
            listener = null;
            thread = null;
        }

        private void Loop()
        {
            while (running)
            {
                HttpListenerContext context;
                try { context = listener!.GetContext(); }
                catch (Exception) { break; } // the listener was stopped
                ThreadPool.QueueUserWorkItem(_ => Serve(context, generation));
            }
        }

        private static void Serve(HttpListenerContext context, int generation)
        {
            try { Handle(context, generation); }
            catch (Exception ex)
            {
                try { Write(context, 500, TestBridge.Fail($"{ex.GetType().Name}: {ex.Message}")); }
                catch (Exception) { try { context.Response.Abort(); } catch (Exception) { } }
            }
        }

        private static void Handle(HttpListenerContext context, int generation)
        {
            var request = context.Request;
            var path = request.Url.AbsolutePath.TrimEnd('/');
            switch (path)
            {
                case "/health" when request.HttpMethod == "GET":
                    Write(context, 200, TestBridge.HealthJson());
                    return;
                case "/events" when request.HttpMethod == "GET":
                    long.TryParse(request.QueryString["since"], out var since);
                    Write(context, 200, TestBridgeEvents.Since(since));
                    return;
                case "/call" when request.HttpMethod == "POST":
                    // Loopback is reachable from web pages too. Reject browser-origin calls before reading
                    // or queuing work, and require a non-simple media type so HTML forms cannot mutate state.
                    if (request.Headers["Origin"] != null || request.Headers["Sec-Fetch-Site"] != null)
                    {
                        Write(context, 403, TestBridge.Fail("Browser-origin calls are refused; use the CLI or MCP bridge client."));
                        return;
                    }
                    var mediaType = (request.ContentType ?? "").Split(';')[0].Trim();
                    if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                    {
                        Write(context, 415, TestBridge.Fail("POST /call requires Content-Type: application/json."));
                        return;
                    }
                    Write(context, 200, Call(request, generation));
                    return;
                default:
                    Write(context, 404, TestBridge.Fail("Unknown endpoint: use GET /health, POST /call or GET /events?since=N."));
                    return;
            }
        }

        private static JObject Call(HttpListenerRequest request, int generation)
        {
            string body;
            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8)) body = reader.ReadToEnd();
            JObject call;
            try { call = BridgeControl.ControlGate.PrepareBody(JObject.Parse(body)); }
            catch (JsonException ex) { return TestBridge.Fail("The body is not a JSON object: " + ex.Message); }
            // A client that knew another session must reconnect first: nothing is queued or numbered for it.
            var expect = call["expect_session"] is JValue e && e.Type == JTokenType.String ? e.Value<string>() : null;
            var session = TestBridge.Session;
            if (expect != null && expect != session)
                return new JObject { ["ok"] = false, ["session"] = session, ["error"] = $"the bridge restarted (session {session}); reconnect first." };
            var cmd = call.Value<string>("cmd");
            if (string.IsNullOrEmpty(cmd)) return TestBridge.Fail("The body has no \"cmd\".");
            var args = call["args"] as JObject ?? new JObject();
            var timeout = 10.0;
            if (args["timeout_s"] is JValue t && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)) timeout = Math.Min(600.0, Math.Max(1.0, t.Value<double>()));
            return TestBridge.Call(cmd!, args, timeout, generation);
        }

        private static void Write(HttpListenerContext context, int status, JObject reply)
        {
            // The bridge's session, next to ok in every reply.
            var ok = reply.Property("ok");
            if (ok != null && reply.Property("session") == null) ok.AddAfterSelf(new JProperty("session", TestBridge.Session));
            var bytes = Encoding.UTF8.GetBytes(reply.ToString(Formatting.None));
            var response = context.Response;
            response.StatusCode = status;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.OutputStream.Close();
        }
    }

    // The last 2,000 events for /events: every line the mod logs, every notice and phone text, and Unity errors and
    // exceptions from anywhere in the game. Written from any thread.
    internal static class TestBridgeEvents
    {
        private const int Capacity = 2000;
        private static readonly object gate = new object();
        private static readonly Queue<JObject> events = new Queue<JObject>();
        private static long next = 1;
        private static bool hooked;

        internal static void Hook()
        {
            if (hooked) return;
            hooked = true;
            Log.Listener = Add;
            UnityEngine.Application.logMessageReceivedThreaded += OnUnityLog;
            Notifications.onShow += OnGameNotice;
        }

        internal static void Unhook()
        {
            if (!hooked) return;
            hooked = false;
            Log.Listener = null;
            UnityEngine.Application.logMessageReceivedThreaded -= OnUnityLog;
            Notifications.onShow -= OnGameNotice;
        }

        // The game's and other mods' notices (a fine, a theft, a pay slip), as the screen shows them: a player reads
        // them as they pop up, so they are events too, and the last ones are kept by game minute for the bank lines.
        private const int GameNoticeCount = 200;
        private static readonly Queue<(int? Day, string? Time, string Text)> gameNotices = new Queue<(int?, string?, string)>();
        private static object? noticeGame, noticeState;

        // Day/time repeat after loads. A notice belongs only to the game and mod-state objects
        // that observed it, even when the same character or save name is loaded again.
        private static void NoticeSession()
        {
            var game = SaveGameManager.Current;
            var state = FoodTrucksSession.CurrentState;
            if (ReferenceEquals(game, noticeGame) && ReferenceEquals(state, noticeState)) return;
            gameNotices.Clear();
            noticeGame = game; noticeState = state;
        }

        private static void OnGameNotice(NotificationType type, string key, Dictionary<string, string> data, float seconds,
            string duplicateIdentifier, Action onClick, bool sound, bool track)
        {
            if (Notices.Showing) return;   // the mod's own, already in as "notice"
            var text = Notices.Text(key, data);
            Add("notice", "Notice: " + text);
            var (day, time) = TestBridge.ClockNow();
            lock (gate)
            {
                NoticeSession();
                gameNotices.Enqueue((day, time, text));
                while (gameNotices.Count > GameNoticeCount) gameNotices.Dequeue();
            }
        }

        // The game's notices shown at that game minute, oldest first.
        internal static List<string> GameNoticesAt(int day, string time)
        {
            lock (gate)
            {
                NoticeSession();
                return gameNotices.Where(n => n.Day == day && n.Time == time).Select(n => n.Text).ToList();
            }
        }

        private static void OnUnityLog(string condition, string stackTrace, UnityEngine.LogType type)
        {
            if (type != UnityEngine.LogType.Error && type != UnityEngine.LogType.Exception && type != UnityEngine.LogType.Assert) return;
            // The mod's own error lines are already in as "error".
            if (Log.Writing) return;
            var text = type == UnityEngine.LogType.Exception && !string.IsNullOrEmpty(stackTrace) ? condition + "\n" + stackTrace : condition;
            Add("unity_error", text);
        }

        internal static void Clear()
        {
            lock (gate)
            {
                events.Clear();
                gameNotices.Clear();
                noticeGame = noticeState = null;
                next = 1;
            }
        }

        internal static void Add(string kind, string text)
        {
            var (day, time) = TestBridge.ClockNow();
            lock (gate)
            {
                events.Enqueue(new JObject
                {
                    ["seq"] = next++,
                    ["real"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    ["day"] = day,
                    ["time"] = time,
                    ["kind"] = kind,
                    ["text"] = text,
                });
                while (events.Count > Capacity) events.Dequeue();
            }
        }

        // The events numbered `since` and later; `next` is the number to ask from next time. Numbers start at 1.
        internal static JObject Since(long since)
        {
            var list = new JArray();
            long dropped = 0;
            long nextSeq;
            since = Math.Max(since, 1);
            lock (gate)
            {
                nextSeq = next;
                var oldest = events.Count == 0 ? next : events.Peek().Value<long>("seq");
                // Events from `since` on that fell out of the buffer before this read.
                if (since < oldest) dropped = oldest - since;
                foreach (var e in events)
                    if (e.Value<long>("seq") >= since) list.Add(e.DeepClone());
            }
            return new JObject { ["ok"] = true, ["next"] = nextSeq, ["dropped"] = dropped, ["events"] = list };
        }
    }
}
