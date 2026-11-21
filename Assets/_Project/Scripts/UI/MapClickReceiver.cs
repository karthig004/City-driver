using UnityEngine;
using UnityEngine.EventSystems;

namespace CityDriver.UI
{
    /// <summary>Sits on the map image and forwards taps to the full-map controller (which stays active while the panel is closed).</summary>
    public sealed class MapClickReceiver : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private FullMapView target;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (target != null) target.OnMapClicked(eventData);
        }
    }
}
