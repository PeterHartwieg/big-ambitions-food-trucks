using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AgentRigInput
{
    // A bounded per-interaction record: one entry per frame with what was exposed and what read it. Never holds tokens.
    public sealed class MouseTrace
    {
        public const int MaxEntries = 600;
        public string Id { get; }
        public string Session { get; }
        public bool Truncated { get; private set; }
        public string EndReason { get; set; }
        private readonly List<JObject> entries = new List<JObject>();

        public MouseTrace(string id, string session) { Id = id; Session = session; }

        public void Add(JObject entry)
        {
            if (entries.Count >= MaxEntries) { Truncated = true; return; }
            entries.Add(entry);
        }

        public JObject Json() => new JObject
        {
            ["trace_id"] = Id,
            ["end_reason"] = EndReason,
            ["trace_truncated"] = Truncated,
            ["trace"] = new JArray(entries.Select(e => e.DeepClone())),
        };
    }

    // The last few traces of this bridge session, by id.
    public sealed class TraceStore
    {
        public const int Kept = 16;
        private readonly LinkedList<MouseTrace> traces = new LinkedList<MouseTrace>();

        public void Add(MouseTrace trace)
        {
            traces.AddLast(trace);
            while (traces.Count > Kept) traces.RemoveFirst();
        }

        public MouseTrace Find(string id, string session)
        {
            var t = traces.FirstOrDefault(x => x.Id == id);
            if (t == null) throw new MouseError("mouse_unknown_trace", $"no trace '{id}' (the rig keeps the last {Kept} of this session)");
            if (t.Session != session) throw new MouseError("mouse_unknown_trace", $"trace '{id}' belongs to an earlier bridge session");
            return t;
        }

        public void Clear() => traces.Clear();
    }
}
