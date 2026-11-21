using UnityEngine;
using UnityEngine.UI;

namespace CityDriver.Game
{
    /// <summary>Put on a RawImage that displays <see cref="CarPreview.Texture"/>; keeps the preview camera running and lets the player drag to turn the car.</summary>
    public sealed class PreviewImage : MonoBehaviour, UnityEngine.EventSystems.IDragHandler, UnityEngine.EventSystems.IBeginDragHandler, UnityEngine.EventSystems.IEndDragHandler
    {
        private RawImage img;
        private void Awake() { img = GetComponent<RawImage>(); }
        private void Update()
        {
            var p = CarPreview.Instance; if (p == null) return;
            if (img != null && img.texture != p.Texture) img.texture = p.Texture;
            p.Touch();
        }
        public void OnBeginDrag(UnityEngine.EventSystems.PointerEventData e) { if (CarPreview.Instance != null) CarPreview.Instance.AutoRotate = false; }
        public void OnDrag(UnityEngine.EventSystems.PointerEventData e) { if (CarPreview.Instance != null) CarPreview.Instance.Rotate(-e.delta.x * 0.45f); }
        public void OnEndDrag(UnityEngine.EventSystems.PointerEventData e) { if (CarPreview.Instance != null) CarPreview.Instance.AutoRotate = true; }
    }
}
