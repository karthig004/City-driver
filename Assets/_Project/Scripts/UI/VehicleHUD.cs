using CityDriver.Vehicle;
using UnityEngine;
using UnityEngine.UI;

namespace CityDriver.UI
{
    /// <summary>
    /// Minimal driving HUD for Phase 4: speed (km/h), gear and vehicle condition. Text is only rewritten when the shown
    /// value changes, using pre-built strings, so it allocates nothing per frame.
    /// The full HUD (wallet, mission, minimap) arrives in Phase 10 and can replace this component.
    /// </summary>
    public sealed class VehicleHUD : MonoBehaviour
    {
        [SerializeField] private VehicleController vehicle;
        [SerializeField] private Text speedText;
        [SerializeField] private Text gearText;
        [SerializeField] private Text conditionText;
        [SerializeField] private Image conditionFill;

        [Header("Condition colours")]
        [SerializeField] private Color good = new Color(0.30f, 0.85f, 0.45f);
        [SerializeField] private Color warning = new Color(1f, 0.75f, 0.15f);
        [SerializeField] private Color critical = new Color(0.95f, 0.25f, 0.2f);

        private const int MaxSpeed = 300;
        private static readonly string[] SpeedStrings = BuildNumberTable(MaxSpeed, null);
        private static readonly string[] ConditionStrings = BuildNumberTable(100, "CONDITION ");

        private int shownSpeed = -1;
        private int shownCondition = -1;
        private string shownGear;

        public void SetVehicle(VehicleController newVehicle)
        {
            vehicle = newVehicle;
            shownSpeed = shownCondition = -1;
            shownGear = null;
        }

        private void Update() => Refresh();

        /// <summary>Updates the displayed values from the vehicle. Called every frame; public for tests.</summary>
        public void Refresh()
        {
            if (vehicle == null) return;

            int speed = Mathf.Min(MaxSpeed, Mathf.RoundToInt(vehicle.SpeedKmh));
            if (speed != shownSpeed && speedText != null) { shownSpeed = speed; speedText.text = SpeedStrings[speed]; }

            string gear = vehicle.GearLabel;
            if (!ReferenceEquals(gear, shownGear) && gearText != null) { shownGear = gear; gearText.text = gear; }

            int condition = Mathf.Clamp(Mathf.RoundToInt(vehicle.Condition01 * 100f), 0, 100);
            if (condition != shownCondition)
            {
                shownCondition = condition;
                if (conditionText != null) conditionText.text = ConditionStrings[condition] + "%";
                if (conditionFill != null)
                {
                    conditionFill.fillAmount = condition / 100f;
                    conditionFill.color = condition > 60 ? good : condition > 30 ? warning : critical;
                }
            }
        }

        private static string[] BuildNumberTable(int max, string prefix)
        {
            var table = new string[max + 1];
            for (int i = 0; i <= max; i++) table[i] = prefix == null ? i.ToString() : prefix + i;
            return table;
        }
    }
}
