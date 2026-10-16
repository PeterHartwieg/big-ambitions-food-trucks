#nullable enable
using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FoodTrucks
{
    // A shift bar on the Week plan tab's grid (BizManWeekPlan.Bar), dragged as the game's schedule screen drags its work
    // shift bars (WorkShiftDrag, WorkShiftSliderHandle, snapped to whole hours as WorkShiftHelper.SnapSliderToHour): the
    // bar moves, an end resizes it, between `min` and `max` (its weekday's hours on the grid) and 1 to `cap` hours long.
    // The new hours are applied when the drag ends; a right-click removes the shift. The mod's own handler, as the game's
    // write the open building's schedule.
    internal sealed class ShiftBarDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        private RectTransform? bar, track;
        // 0 moves the bar, -1 drags its start, 1 its end.
        private int mode;
        private int start, end, from, span, min, max, cap;
        private int startAtDrag, endAtDrag;
        private float xAtDrag;
        private bool dragging;
        private Action<int, int>? moved;
        private Action? removed;

        internal void Set(RectTransform bar, RectTransform track, int mode, int start, int end, int from, int span, int min, int max, int cap,
            Action<int, int> moved, Action removed)
        {
            this.bar = bar;
            this.track = track;
            this.mode = mode;
            this.start = start;
            this.end = end;
            this.from = from;
            this.span = Math.Max(1, span);
            this.min = min;
            this.max = max;
            this.cap = Math.Max(1, cap);
            this.moved = moved;
            this.removed = removed;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (track == null || !Local(eventData, out var x)) return;
            dragging = true;
            xAtDrag = x;
            startAtDrag = start;
            endAtDrag = end;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || track == null || bar == null || !Local(eventData, out var x)) return;
            var hourWidth = track.rect.width / span;
            if (hourWidth <= 0f) return;
            var hours = Mathf.RoundToInt((x - xAtDrag) / hourWidth);
            var (s, e) = (startAtDrag, endAtDrag);
            if (mode == 0)
            {
                var length = e - s;
                s = Mathf.Clamp(s + hours, min, max - length);
                e = s + length;
            }
            else if (mode < 0) s = Mathf.Clamp(s + hours, Math.Max(min, e - cap), e - 1);
            else e = Mathf.Clamp(e + hours, s + 1, Math.Min(max, s + cap));
            if (s == start && e == end) return;
            start = s;
            end = e;
            var anchorMin = bar.anchorMin;
            var anchorMax = bar.anchorMax;
            bar.anchorMin = new Vector2((float)(start - from) / span, anchorMin.y);
            bar.anchorMax = new Vector2((float)(end - from) / span, anchorMax.y);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!dragging) return;
            dragging = false;
            if (start == startAtDrag && end == endAtDrag) return;
            try { moved?.Invoke(start, end); }
            catch (Exception ex) { Log.Error("Week plan: shift drag: " + ex); }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Right || dragging) return;
            try { removed?.Invoke(); }
            catch (Exception ex) { Log.Error("Week plan: shift remove: " + ex); }
        }

        private bool Local(PointerEventData eventData, out float x)
        {
            x = 0f;
            if (track == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(track, eventData.position, eventData.pressEventCamera, out var point))
                return false;
            x = point.x;
            return true;
        }
    }
}
