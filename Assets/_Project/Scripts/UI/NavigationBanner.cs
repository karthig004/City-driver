using CityDriver.Navigation;
using CityDriver.World;
using UnityEngine;
using UnityEngine.UI;

namespace CityDriver.UI
{
    /// <summary>Top-of-screen GPS banner: next-turn instruction with a turn arrow, destination name and distance remaining.</summary>
    public sealed class NavigationBanner : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Text instructionText;
        [SerializeField] private Text detailText;
        [SerializeField] private RectTransform arrow;      // rotates to show the turn direction
        [SerializeField, Min(0.05f)] private float refreshInterval = 0.25f;

        private float next;
        private string lastInstruction, lastDetail;

        private void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + refreshInterval;

            NavigationService nav = NavigationService.Instance;
            bool show = nav != null && nav.HasDestination;
            if (root != null && root.activeSelf != show) root.SetActive(show);
            if (!show) return;

            string instruction = nav.Instruction;
            float km = nav.DistanceRemaining / 1000f;
            string detail = $"{nav.DestinationLabel}  -  {(km >= 1f ? km.ToString("0.0") + " km" : Mathf.RoundToInt(nav.DistanceRemaining / 10f) * 10 + " m")}";
            if (instructionText != null && instruction != lastInstruction) { lastInstruction = instruction; instructionText.text = instruction; }
            if (detailText != null && detail != lastDetail) { lastDetail = detail; detailText.text = detail; }
            if (arrow != null) arrow.localRotation = Quaternion.Euler(0f, 0f, -Angle(nav.NextTurn));
        }

        private static float Angle(TurnType t)
        {
            switch (t)
            {
                case TurnType.SlightLeft: return -35f;
                case TurnType.Left: return -90f;
                case TurnType.SharpLeft: return -135f;
                case TurnType.SlightRight: return 35f;
                case TurnType.Right: return 90f;
                case TurnType.SharpRight: return 135f;
                case TurnType.UTurn: return 180f;
                default: return 0f;
            }
        }
    }
}
