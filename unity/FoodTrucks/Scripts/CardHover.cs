#nullable enable
using UnityEngine;
using UnityEngine.EventSystems;

namespace FoodTrucks
{
    // The hover zoom of a Street food page card, in place of the copied warehouse card's ButtonEffects (Peter, 16 October
    // 2026: hovering a button or field inside a card made it shrink and grow again, and near its edge it shook). The card
    // grows when the pointer comes onto it and stays grown until the pointer is outside the card's unscaled outline, so a
    // button inside it, or the margin the zoom itself adds, doesn't count as leaving.
    internal sealed class CardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
    {
        internal float HoverScale = 1.02f;
        private const float Speed = 12f;
        private bool hovered;
        private Vector2 pointer;
        private Camera? eventCamera;

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            pointer = eventData.position;
            eventCamera = eventData.enterEventCamera;
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            pointer = eventData.position;
            eventCamera = eventData.enterEventCamera ?? eventCamera;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            pointer = eventData.position;
            if (!Inside()) hovered = false;
        }

        private void OnDisable()
        {
            hovered = false;
            transform.localScale = Vector3.one;
        }

        private void Update()
        {
            if (hovered && !Inside()) hovered = false;
            var target = hovered ? HoverScale : 1f;
            var scale = transform.localScale.x;
            if (Mathf.Abs(scale - target) < 0.0005f)
            {
                if (scale != target) transform.localScale = new Vector3(target, target, 1f);
                return;
            }
            var next = Mathf.MoveTowards(scale, target, Speed * Time.unscaledDeltaTime * Mathf.Abs(HoverScale - 1f) * 5f);
            transform.localScale = new Vector3(next, next, 1f);
        }

        // Whether the pointer is within the card as it stands unscaled: the point in the card's local space, scaled back
        // by the zoom, inside its rect.
        private bool Inside()
        {
            if (!(transform is RectTransform rect)) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, pointer, eventCamera, out var local)) return false;
            var scale = transform.localScale;
            return rect.rect.Contains(new Vector2(local.x * scale.x, local.y * scale.y));
        }
    }
}
