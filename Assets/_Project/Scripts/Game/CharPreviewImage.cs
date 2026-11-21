using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CityDriver.Game
{
    /// <summary>Put on a RawImage that shows the <see cref="CharacterPreview"/>; keeps the preview camera running and lets the player drag to turn the character.</summary>
    public sealed class CharPreviewImage : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
    {
        private RawImage img;
        private void Awake() { img = GetComponent<RawImage>(); }
        private void Update()
        {
            var p = CharacterPreview.Instance; if (p == null) return;
            if (img != null && img.texture != p.Texture) img.texture = p.Texture;
            p.Touch();
        }
        public void OnBeginDrag(PointerEventData e) { if (CharacterPreview.Instance != null) CharacterPreview.Instance.AutoRotate = false; }
        public void OnDrag(PointerEventData e) { if (CharacterPreview.Instance != null) CharacterPreview.Instance.Rotate(-e.delta.x * 0.5f); }
        public void OnEndDrag(PointerEventData e) { if (CharacterPreview.Instance != null) CharacterPreview.Instance.AutoRotate = true; }
    }
}
