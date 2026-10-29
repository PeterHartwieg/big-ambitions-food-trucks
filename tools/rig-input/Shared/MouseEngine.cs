using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AgentRigInput
{
    // What the engine needs from the game process. The runtime implements it with Unity; tests with a fake.
    public interface IMouseHost
    {
        long Frame { get; }
        // Unscaled real time in seconds: a paused game clock must not pause gestures or watchdogs.
        double Now { get; }
        int ScreenWidth { get; }
        int ScreenHeight { get; }
        // Installs the virtual pointer module, device and legacy shim. Throws MouseError if it cannot.
        void Activate();
        // Restores real input. Safe to call more than once.
        void Restore();
        // Delivers pointer up / end drag to captured UI now, for cleanup that cannot wait for frames.
        void ForceRelease();
        // Extra trace fields for the frame that just ran (pointer state, read counters, native observers).
        JObject ObserveFrame();
        // The last frame a native release observer (Schedule's OnSlideEnd) returned in; -1 if none.
        long NativeReleaseFrame { get; }
        // The guarded UI escape, run in the release frame.
        JToken RunAfterRelease();
    }

    public sealed class MouseInteraction
    {
        public string Id, Kind, Session, Owner;
        public double IdleDeadline, HardDeadline;
        public MouseTrace Trace;
        public MouseCommand Pending;
        public bool Releasing;
        public string ReleaseReason;
        public double ReleaseDeadline;
        public Action EndReservation;
        public long StartFrame;
    }

    public sealed class MouseCommand
    {
        public string Op;
        public bool EndsInteraction, AfterRelease;
        // True when a native release observer covers the press target; null for generic targets.
        public bool? ReleaseObserved;
        public long DownFrame = -1, LastMoveFrame = -1, UpFrame = -1, NativeReleaseFrame = -1, CompletedFrame = -1, AfterReleaseFrame = -1;
        public ScreenPoint Start, End;
        public int FramesApplied;
        public JToken AfterReleaseResult;
        public string AfterReleaseError;
        public JObject Extra;
        internal bool Done;
        internal JObject Result;
        internal MouseError Error;
        internal int AppliedAtStart;
    }

    // The single virtual mouse of a rig process: one owned interaction at a time, one sample per frame, and one
    // cleanup path for every way an interaction can end.
    public sealed class MouseEngine
    {
        public const double ReleaseDrainSeconds = 2;
        private readonly IMouseHost host;
        private int counter;
        public MouseTimeline Timeline { get; } = new MouseTimeline();
        public MouseInteraction Current { get; private set; }
        public TraceStore Traces { get; } = new TraceStore();
        public string LastError { get; private set; }

        public MouseEngine(IMouseHost host) { this.host = host; }

        public bool Active => Current != null;

        // Starts a command, creating the interaction if there is none. `plan` gets whether the interaction is new.
        public MouseCommand Begin(string op, string kind, string session, string owner, Func<bool, ScreenPoint, List<MouseSample>> plan,
                                  bool endsInteraction, bool afterRelease, bool? releaseObserved, Func<Action> reserve, double hardSeconds)
        {
            var current = Current;
            if (current != null)
            {
                if (current.Owner != owner || current.Session != session)
                    throw new MouseError("mouse_busy", "another owner's virtual mouse interaction is active");
                if (current.Releasing) throw new MouseError("mouse_busy", "the virtual mouse is releasing");
                if (current.Pending != null) throw new MouseError("mouse_busy", $"mouse {current.Pending.Op} is still running");
                if (op == "drag" && Timeline.State.Held != 0)
                    throw new MouseError("mouse_busy", "a button is held: release it before a self-contained drag");
            }
            var fresh = current == null;
            var position = new ScreenPoint(Timeline.State.X, Timeline.State.Y);
            var samples = plan(fresh, position);
            if (fresh)
            {
                var end = reserve();
                var id = $"{kind}-{++counter}";
                current = new MouseInteraction
                {
                    Id = id, Kind = kind, Session = session, Owner = owner, Trace = new MouseTrace(id, session),
                    EndReservation = end, StartFrame = host.Frame,
                };
                Current = current;
                Traces.Add(current.Trace);
                try { host.Activate(); }
                catch (Exception ex)
                {
                    Finish("activation failed: " + ex.Message);
                    LastError = ex.Message;
                    throw ex is MouseError ? ex : new MouseError("rig_mouse_unavailable", ex.Message);
                }
            }
            var command = new MouseCommand
            {
                Op = op, EndsInteraction = endsInteraction, AfterRelease = afterRelease, ReleaseObserved = releaseObserved,
                AppliedAtStart = Timeline.Applied,
            };
            Timeline.Enqueue(samples);
            current.Pending = command;
            current.HardDeadline = host.Now + hardSeconds;
            current.IdleDeadline = double.MaxValue;
            return command;
        }

        // Polled by the bridge each frame: null while running, the reply when done; throws the refusal it ended with.
        public JObject Poll(MouseCommand command)
        {
            if (!command.Done) return null;
            if (command.Error != null) throw command.Error;
            return command.Result;
        }

        // Before every consumer of the frame: watchdogs, then the next sample.
        public void EarlyFrame()
        {
            var current = Current;
            if (current == null) return;
            var now = host.Now;
            if (current.Releasing && now > current.ReleaseDeadline)
            {
                host.ForceRelease();
                Finish(current.ReleaseReason + "; release drain timed out");
                return;
            }
            if (!current.Releasing && (now > current.HardDeadline || now > current.IdleDeadline))
                Cancel(now > current.HardDeadline ? "gesture watchdog" : "idle hold watchdog", "mouse_timeout", true);
            if (Current == null) return;
            if (!Timeline.Step(host.Frame, now)) return;
            var pending = Current.Pending;
            if (pending == null) return;
            switch (Timeline.State.Phase)
            {
                case "down": pending.DownFrame = host.Frame; break;
                case "move": case "hold": pending.LastMoveFrame = host.Frame; break;
                case "up": pending.UpFrame = host.Frame; break;
            }
        }

        // After Update and coroutines, before LateUpdate: trace, the after-release action, completion.
        public void LateFrame()
        {
            var current = Current;
            if (current == null) return;
            var frame = host.Frame;
            var state = Timeline.State;
            var entry = new JObject
            {
                ["frame"] = frame, ["phase"] = state.Frame == frame && Timeline.LastSampleFrame == frame ? state.Phase : "held",
                ["x"] = Math.Round(state.X, 2), ["y"] = Math.Round(state.Y, 2), ["buttons"] = new JArray(state.Buttons()),
            };
            JObject observed = null;
            try { observed = host.ObserveFrame(); }
            catch (Exception ex) { entry["observe_error"] = ex.Message; }
            if (observed != null) foreach (var p in observed.Properties()) entry[p.Name] = p.Value;
            current.Trace.Add(entry);

            var pending = current.Pending;
            var native = host.NativeReleaseFrame;
            if (pending != null && pending.UpFrame >= 0 && native >= pending.UpFrame && pending.NativeReleaseFrame < 0)
                pending.NativeReleaseFrame = native;
            if (pending != null && pending.AfterRelease && pending.AfterReleaseFrame < 0 && pending.NativeReleaseFrame == frame)
            {
                pending.AfterReleaseFrame = frame;
                try { pending.AfterReleaseResult = host.RunAfterRelease(); }
                catch (Exception ex) { pending.AfterReleaseError = ex.Message; }
            }
            if (Timeline.Pending != 0 || Timeline.LastSampleFrame != frame) return;
            if (current.Releasing) { Finish(current.ReleaseReason); return; }
            if (pending == null) return;
            pending.CompletedFrame = frame;
            pending.FramesApplied = Timeline.Applied - pending.AppliedAtStart;
            current.Pending = null;
            current.HardDeadline = double.MaxValue;
            current.IdleDeadline = host.Now + MouseArgs.HoldTimeoutSeconds;
            MouseError error = null;
            if (pending.AfterRelease && pending.AfterReleaseFrame < 0)
                error = new MouseError("mouse_no_native_release", "the native release was not observed, so the after-release escape did not run");
            else if (pending.AfterReleaseError != null)
                error = new MouseError("mouse_after_release_failed", pending.AfterReleaseError);
            var ends = pending.EndsInteraction || (pending.Op == "up" && state.Held == 0);
            if (ends || error != null) Finish(error != null ? error.Message : pending.Op + " completed");
            pending.Result = Reply(pending, current);
            pending.Error = error;
            pending.Done = true;
        }

        private JObject Reply(MouseCommand c, MouseInteraction interaction)
        {
            var s = Timeline.State;
            var reply = new JObject
            {
                ["interaction"] = interaction.Id,
                ["completed"] = true,
                ["active"] = Current != null,
                ["frame"] = host.Frame,
                ["phase"] = s.Phase,
                ["position"] = new JObject { ["x"] = Math.Round(s.X, 2), ["y"] = Math.Round(s.Y, 2) },
                ["buttons"] = new JArray(Current != null ? s.Buttons() : new[] { false, false, false }),
                ["frames_applied"] = c.FramesApplied,
                ["completed_frame"] = c.CompletedFrame,
                ["screen"] = Screen(),
                ["trace_id"] = interaction.Trace.Id,
            };
            if (c.DownFrame >= 0) reply["down_frame"] = c.DownFrame;
            if (c.LastMoveFrame >= 0) reply["last_move_frame"] = c.LastMoveFrame;
            if (c.UpFrame >= 0)
            {
                reply["up_frame"] = c.UpFrame;
                reply["native_release_frame"] = c.NativeReleaseFrame >= 0 ? (JToken)c.NativeReleaseFrame : JValue.CreateNull();
                reply["native_release_observed"] = c.ReleaseObserved == null ? JValue.CreateNull() : (JToken)(c.NativeReleaseFrame >= 0);
            }
            if (c.Op == "drag")
            {
                reply["start"] = Point(c.Start);
                reply["end"] = Point(c.End);
                reply["after_release"] = !c.AfterRelease ? JValue.CreateNull() : new JObject
                {
                    ["op"] = AfterRelease.Escape, ["phase"] = AfterRelease.Phase,
                    ["frame"] = c.AfterReleaseFrame >= 0 ? (JToken)c.AfterReleaseFrame : JValue.CreateNull(),
                    ["same_frame"] = c.AfterReleaseFrame >= 0 && c.AfterReleaseFrame == c.NativeReleaseFrame,
                    ["result"] = c.AfterReleaseResult?.DeepClone(),
                };
            }
            if (c.Extra != null) foreach (var p in c.Extra.Properties()) reply[p.Name] = p.Value;
            return reply;
        }

        public JObject Screen() => new JObject { ["width"] = host.ScreenWidth, ["height"] = host.ScreenHeight, ["origin"] = "bottom-left" };
        public static JObject Point(ScreenPoint p) => new JObject { ["x"] = Math.Round(p.X, 2), ["y"] = Math.Round(p.Y, 2) };

        // The owner's explicit release: idempotent, and available after the target or scene has gone.
        public MouseCommand Release(string owner, string session)
        {
            var current = Current;
            if (current == null) return null;
            if (current.Owner != owner) throw new MouseError("mouse_busy", "another owner's virtual mouse interaction is active");
            // A second release while the first drains waits for the same drain.
            if (current.Releasing && current.Pending?.Op == "release") return current.Pending;
            var command = new MouseCommand { Op = "release", AppliedAtStart = Timeline.Applied };
            Cancel("released by its owner", "mouse_cancelled", true);
            if (Current == null) { command.Done = true; command.Result = Released(current, true); return command; }
            Current.Pending = command;
            return command;
        }

        private JObject Released(MouseInteraction interaction, bool released) => new JObject
        {
            ["released"] = released, ["interaction"] = interaction?.Id, ["active"] = Current != null,
            ["buttons"] = new JArray(false, false, false), ["frame"] = host.Frame, ["trace_id"] = interaction?.Trace.Id,
        };

        public JObject ReleasedNothing() => Released(null, false);

        // Every end other than normal completion. Graceful: expose a release frame and drain before restoring input.
        // Immediate (bridge stop, quit, exceptions): deliver the release to captured UI now and restore at once.
        public void Cancel(string reason, string code, bool graceful)
        {
            var current = Current;
            if (current == null) return;
            var pending = current.Pending;
            if (pending != null && pending.Op != "release")
            {
                pending.Error = new MouseError(code, reason);
                pending.Done = true;
                current.Pending = null;
            }
            LastError = code + ": " + reason;
            if (!graceful)
            {
                try { host.ForceRelease(); }
                catch (Exception ex) { LastError += "; force release: " + ex.Message; }
                Finish(reason);
                return;
            }
            if (current.Releasing) return;
            Timeline.Cancel();
            current.Releasing = true;
            current.ReleaseReason = reason;
            current.ReleaseDeadline = host.Now + ReleaseDrainSeconds;
            var s = Timeline.State;
            Timeline.Enqueue(new List<MouseSample>
            {
                new MouseSample(s.X, s.Y, 0, "release"),
                new MouseSample(s.X, s.Y, 0, "settle"),
                new MouseSample(s.X, s.Y, 0, "settle"),
            });
        }

        private void Finish(string reason)
        {
            var current = Current;
            if (current == null) return;
            Current = null;
            Timeline.Cancel();
            Timeline.State.Reset(Timeline.State.X, Timeline.State.Y);
            current.Trace.EndReason = reason;
            try { host.Restore(); }
            catch (Exception ex) { LastError = "restore: " + ex.Message; }
            try { current.EndReservation?.Invoke(); }
            catch (Exception ex) { LastError = "reservation: " + ex.Message; }
            current.EndReservation = null;
            var pending = current.Pending;
            if (pending != null)
            {
                current.Pending = null;
                if (pending.Op == "release") { pending.Result = Released(current, true); pending.Done = true; }
                else if (!pending.Done) { pending.Error = new MouseError("mouse_cancelled", reason); pending.Done = true; }
            }
        }

        // Session reset: every interaction and trace of the old session goes.
        public void ResetSession(string reason)
        {
            Cancel(reason, "mouse_cancelled", false);
            Traces.Clear();
        }
    }
}
