using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityDriver.Missions
{
    /// <summary>
    /// Testing keys for waypoints. Compiled into Editor and Development builds only, so release builds never ship them:
    /// F2 = cycle which single waypoint is shown, F3 = toggle "show every waypoint".
    /// </summary>
    public sealed class MarkerDebugControls : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly List<string> ids = new List<string>();
        private int index = -1;

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || MarkerDirector.Instance == null) return;

            if (kb.f2Key.wasPressedThisFrame)
            {
                if (ids.Count == 0) CollectIds();
                index = (index + 1) % (ids.Count + 1);          // last step = hide all
                MarkerDirector.Instance.ShowOnly(index < ids.Count ? ids[index] : null);
            }

            if (kb.f3Key.wasPressedThisFrame)
                MarkerDirector.Instance.SetDebugShowAll(!MarkerDirector.Instance.DebugShowAll);
        }

        private void CollectIds()
        {
            foreach (var m in LocationMarker.All)
                if (m.Kind == LocationKind.Pickup || m.Kind == LocationKind.Dropoff) ids.Add(m.LocationId);
            ids.Sort();
        }
#endif
    }
}
