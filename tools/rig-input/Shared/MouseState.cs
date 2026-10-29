using System;
using System.Collections.Generic;

namespace AgentRigInput
{
    // One Unity frame's worth of virtual mouse input. Positions are Unity screen pixels, origin bottom-left.
    public struct MouseSample
    {
        public double X, Y;
        // Held buttons as a bit mask: bit 0 left, bit 1 right, bit 2 middle.
        public int Held;
        public double ScrollX, ScrollY;
        public string Phase;
        // Earliest unscaled time, in seconds after the batch's first sample, at which this sample may be applied.
        public double NotBefore;

        public MouseSample(double x, double y, int held, string phase, double notBefore = 0, double scrollX = 0, double scrollY = 0)
        {
            X = x; Y = y; Held = held & 7; Phase = phase; NotBefore = notBefore; ScrollX = scrollX; ScrollY = scrollY;
        }
    }

    // What every reader sees during one frame. Down/Up edges last exactly the frame the held state changed.
    public sealed class VirtualMouseState
    {
        public double X { get; private set; }
        public double Y { get; private set; }
        public int Held { get; private set; }
        public int Down { get; private set; }
        public int Up { get; private set; }
        public double ScrollX { get; private set; }
        public double ScrollY { get; private set; }
        public long Frame { get; private set; } = -1;
        public string Phase { get; private set; } = "idle";

        public bool IsHeld(int button) => Valid(button) && (Held & (1 << button)) != 0;
        public bool IsDown(int button) => Valid(button) && (Down & (1 << button)) != 0;
        public bool IsUp(int button) => Valid(button) && (Up & (1 << button)) != 0;
        private static bool Valid(int button) => button >= 0 && button <= 2;

        public void Apply(MouseSample sample, long frame)
        {
            var previous = Held;
            Held = sample.Held & 7;
            Down = Held & ~previous;
            Up = previous & ~Held;
            X = sample.X; Y = sample.Y;
            ScrollX = sample.ScrollX; ScrollY = sample.ScrollY;
            Frame = frame;
            Phase = sample.Phase ?? "sample";
        }

        // A frame without a new sample: the same position and held buttons, no edges, no scroll.
        public void Hold(long frame)
        {
            Down = Up = 0;
            ScrollX = ScrollY = 0;
            Frame = frame;
        }

        // Moves the pointer without changing buttons (a cursor warp request while virtual input is active).
        public void Warp(double x, double y) { X = x; Y = y; }

        public void Reset(double x, double y)
        {
            Held = Down = Up = 0;
            ScrollX = ScrollY = 0;
            X = x; Y = y;
            Phase = "idle";
        }

        public bool[] Buttons() => new[] { IsHeld(0), IsHeld(1), IsHeld(2) };
    }

    // Applies at most one queued sample per frame, before any consumer of that frame reads input. While a sample
    // waits for its time, the previous held state repeats without edges, so a zero-duration gesture still spends
    // one frame per sample and a timed one never collapses two samples into one frame.
    public sealed class MouseTimeline
    {
        private readonly Queue<MouseSample> queue = new Queue<MouseSample>();
        private double? batchStart;
        private long lastFrame = -1;

        public VirtualMouseState State { get; } = new VirtualMouseState();
        public int Pending => queue.Count;
        public long LastSampleFrame { get; private set; } = -1;
        public int Applied { get; private set; }

        public void Enqueue(IEnumerable<MouseSample> samples)
        {
            if (queue.Count != 0) throw new InvalidOperationException("mouse_busy: a timeline is still running");
            foreach (var s in samples) queue.Enqueue(s);
            batchStart = null;
        }

        // Called once per frame, before consumers. Returns true when a new sample was applied this frame.
        public bool Step(long frame, double now)
        {
            if (frame <= lastFrame) return false;
            lastFrame = frame;
            if (queue.Count == 0) { State.Hold(frame); return false; }
            if (batchStart == null) batchStart = now;
            var next = queue.Peek();
            if (now - batchStart.Value + 1e-9 < next.NotBefore) { State.Hold(frame); return false; }
            queue.Dequeue();
            State.Apply(next, frame);
            LastSampleFrame = frame;
            Applied++;
            if (queue.Count == 0) batchStart = null;
            return true;
        }

        public void Cancel()
        {
            queue.Clear();
            batchStart = null;
        }
    }

    public struct ScreenPoint
    {
        public double X, Y;
        public ScreenPoint(double x, double y) { X = x; Y = y; }
    }

    // Sample sequences for the bridge operations. Every sequence starts with a position-only frame where the pointer
    // moves, so hover and raycast are settled before a press.
    public static class Gestures
    {
        public static int Bit(int button) => 1 << button;

        public static List<MouseSample> Prepare(ScreenPoint at, int held, bool fresh)
        {
            var list = new List<MouseSample>();
            // A newly installed pointer module is picked up by the EventSystem one frame before it processes.
            if (fresh) list.Add(new MouseSample(at.X, at.Y, held, "prepare"));
            list.Add(new MouseSample(at.X, at.Y, held, "hover"));
            return list;
        }

        public static List<MouseSample> Move(ScreenPoint from, ScreenPoint to, int held, int frames)
        {
            var list = new List<MouseSample>();
            for (var i = 1; i <= frames; i++)
            {
                var t = (double)i / frames;
                list.Add(new MouseSample(Lerp(from.X, to.X, t), Lerp(from.Y, to.Y, t), held, "move"));
            }
            return list;
        }

        public static List<MouseSample> Down(ScreenPoint at, int held, int button) =>
            new List<MouseSample> { new MouseSample(at.X, at.Y, held | Bit(button), "down") };

        public static List<MouseSample> Up(ScreenPoint at, int held, int button, int settle)
        {
            var after = held & ~Bit(button);
            var list = new List<MouseSample> { new MouseSample(at.X, at.Y, after, "up") };
            for (var i = 0; i < settle; i++) list.Add(new MouseSample(at.X, at.Y, after, "settle"));
            return list;
        }

        public static List<MouseSample> Scroll(ScreenPoint at, int held, double x, double y) => new List<MouseSample>
        {
            new MouseSample(at.X, at.Y, held, "scroll", 0, x, y),
            new MouseSample(at.X, at.Y, held, "settle"),
        };

        // prepare? hover, down, steps moves spread over duration, one full endpoint frame held, up, settle frames.
        public static List<MouseSample> Drag(ScreenPoint from, ScreenPoint to, int button, int steps, double duration, int settle, bool fresh)
        {
            var b = Bit(button);
            var list = Prepare(from, 0, fresh);
            list.Add(new MouseSample(from.X, from.Y, b, "down"));
            for (var i = 1; i <= steps; i++)
            {
                var t = (double)i / steps;
                list.Add(new MouseSample(Lerp(from.X, to.X, t), Lerp(from.Y, to.Y, t), b, "move", duration * t));
            }
            list.Add(new MouseSample(to.X, to.Y, b, "hold", duration));
            list.Add(new MouseSample(to.X, to.Y, 0, "up", duration));
            for (var i = 0; i < settle; i++) list.Add(new MouseSample(to.X, to.Y, 0, "settle", duration));
            return list;
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
