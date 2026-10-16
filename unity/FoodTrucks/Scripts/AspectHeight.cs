#nullable enable
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FoodTrucks
{
    // A layout element whose height follows its width: the width the layout gives it over `Aspect` (width over height),
    // at most `MaxHeight`. The vehicle drawing fills the Menu box's width this way (Peter, 16 October 2026: "bigger and
    // wider"); the layout asks for heights after it has set widths, so the width read here is the one it will draw at.
    internal sealed class AspectHeight : UIBehaviour, ILayoutElement
    {
        internal float Aspect = 3f;
        internal float MaxHeight = 600f;

        private float Height
        {
            get
            {
                var width = ((RectTransform)transform).rect.width;
                return width > 1f && Aspect > 0f ? Mathf.Min(width / Aspect, MaxHeight) : MaxHeight / 2f;
            }
        }

        public float minWidth => -1f;
        public float preferredWidth => -1f;
        public float flexibleWidth => -1f;
        public float minHeight => Height;
        public float preferredHeight => Height;
        public float flexibleHeight => -1f;
        public int layoutPriority => 2;

        public void CalculateLayoutInputHorizontal() { }
        public void CalculateLayoutInputVertical() { }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if (isActiveAndEnabled) LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }
    }
}
