using System.Collections.Generic;
using CityDriver.Save;

namespace CityDriver.Career
{
    /// <summary>
    /// The 10 career levels x 3 mandatory assignments from the design brief, plus the mandatory upgrade gate per level.
    /// Rewards, timers, penalties and gates are first-pass balance values (tunable); the structure is fixed.
    /// </summary>
    public static class CareerCatalog
    {
        public const int LevelCount = 10;
        public const int ShadowPassLevel = 3;        // pass is granted when Level 3 is completed
        public const int WeaponShopLevel = 5;        // weapon shop unlocks when Level 5 is completed

        private static readonly List<MissionDefinition> All = Build();

        public static IReadOnlyList<MissionDefinition> Missions => All;

        public static MissionDefinition Find(string id)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].Id == id) return All[i];
            return null;
        }

        public static List<MissionDefinition> ForLevel(int level) => All.FindAll(m => m.Level == level);

        /// <summary>Mandatory upgrades required to complete a level (design placeholder values).</summary>
        public static IReadOnlyList<UpgradeRequirement> RequirementsFor(int level)
        {
            switch (level)
            {
                case 2: return new[] { new UpgradeRequirement(UpgradeType.Tires, 1) };
                case 3: return new[] { new UpgradeRequirement(UpgradeType.Brakes, 1) };
                case 4: return new[] { new UpgradeRequirement(UpgradeType.Engine, 1) };
                case 5: return new[] { new UpgradeRequirement(UpgradeType.Suspension, 1), new UpgradeRequirement(UpgradeType.Armor, 1) };
                case 6: return new[] { new UpgradeRequirement(UpgradeType.Engine, 2) };
                case 7: return new[] { new UpgradeRequirement(UpgradeType.Tires, 2), new UpgradeRequirement(UpgradeType.Brakes, 2) };
                case 8: return new[] { new UpgradeRequirement(UpgradeType.Armor, 2) };
                case 9: return new[] { new UpgradeRequirement(UpgradeType.Engine, 3), new UpgradeRequirement(UpgradeType.Suspension, 2) };
                default: return System.Array.Empty<UpgradeRequirement>();
            }
        }

        private static MissionDefinition M(int level, int order, string id, string title, MissionKind kind, long reward, long xp, int time,
            string pickup, string dropoff, string brief, bool night = false, bool shadow = false, long penalty = 0)
        {
            return new MissionDefinition
            {
                Id = id, Level = level, Order = order, Title = title, Kind = kind, Reward = reward, Xp = xp, TimeLimitSeconds = time,
                PickupId = pickup, DropoffId = dropoff, NightOnly = night, ShadowRun = shadow, FailPenalty = penalty, Briefing = brief,
                CompletionCriteria = shadow ? "Deliver the passenger to the destination alive and un-captured." : "Reach the destination with the passenger.",
                FailureCriteria = (time > 0 ? "Timer expires. " : "") + (shadow ? "Player or passenger killed, captured, vehicle destroyed, or mission abandoned." : "Mission abandoned or vehicle destroyed."),
            };
        }

        private static List<MissionDefinition> Build()
        {
            return new List<MissionDefinition>
            {
                // Level 1 - Beginner Driver (rewards from the original brief)
                M(1, 1, "L1_first_ride", "First Ride", MissionKind.Taxi, 300, 100, 0, "pickup_a", "dropoff_a", "Pick up your first passenger and take them across town."),
                M(1, 2, "L1_city_trip", "City Trip", MissionKind.Taxi, 600, 150, 420, "pickup_b", "dropoff_b", "A longer fare across the city. Be on time."),
                M(1, 3, "L1_safe_driver", "Safe Driver", MissionKind.Taxi, 500, 150, 0, "pickup_c", "dropoff_c", "Complete the trip without a major collision."),
                // Level 2 - City Driver
                M(2, 1, "L2_multiple_passengers", "Multiple Passengers", MissionKind.Taxi, 700, 200, 0, "pickup_a", "dropoff_b", "Collect two fares and drop both off."),
                M(2, 2, "L2_time_challenge", "Time Challenge", MissionKind.TimeChallenge, 800, 220, 240, "pickup_b", "dropoff_c", "Beat the clock to a busy destination."),
                M(2, 3, "L2_vip_transport", "VIP Transport", MissionKind.Vip, 1000, 250, 0, "pickup_c", "dropoff_a", "Drive a VIP smoothly: no hard braking, no collisions."),
                // Level 3 - The Mysterious Passenger (grants the Shadow Pass at the end of the level)
                M(3, 1, "L3_long_journey", "Long Journey", MissionKind.LongJourney, 1200, 300, 600, "pickup_a", "dropoff_c", "A very long fare across all districts."),
                M(3, 2, "L3_night_passenger", "Night Passenger", MissionKind.NightPassenger, 1300, 320, 0, "pickup_b", "dropoff_a", "A night fare; visibility is low.", night: true),
                M(3, 3, "L3_mysterious_passenger", "The Mysterious Passenger", MissionKind.Mysterious, 1500, 400, 0, "pickup_c", "dropoff_b", "A passenger in a dark coat pays in cash and leaves you a black card.", night: true),
                // Level 4 - Rescue Driver (Shadow Run begins)
                M(4, 1, "L4_first_rescue", "First Rescue", MissionKind.Rescue, 2000, 450, 360, "pickup_a", "dropoff_c", "Unknown caller: get this person out of the city.", night: true, shadow: true, penalty: 800),
                M(4, 2, "L4_warehouse_rescue", "Warehouse Rescue", MissionKind.Rescue, 2300, 500, 420, "pickup_b", "dropoff_a", "Extract a passenger from the industrial district.", night: true, shadow: true, penalty: 900),
                M(4, 3, "L4_first_gang_escape", "First Gang Escape", MissionKind.GangEscape, 2600, 550, 0, "pickup_c", "dropoff_b", "A gang vehicle is on your tail. Lose it.", night: true, shadow: true, penalty: 1000),
                // Level 5 - Wanted Driver
                M(5, 1, "L5_police_chase", "Police Chase", MissionKind.PoliceChase, 3000, 600, 0, "pickup_a", "dropoff_b", "Police have your plate. Reach the safe house.", night: true, shadow: true, penalty: 1200),
                M(5, 2, "L5_emergency_pickup", "Emergency Pickup", MissionKind.EmergencyPickup, 3200, 650, 240, "pickup_b", "dropoff_c", "A critical pickup with a tight timer.", night: true, shadow: true, penalty: 1300),
                M(5, 3, "L5_highway_escape", "Highway Escape", MissionKind.HighwayEscape, 3500, 700, 0, "pickup_c", "dropoff_a", "Escape pursuers along the highway.", night: true, shadow: true, penalty: 1400),
                // Level 6 - Gang Target
                M(6, 1, "L6_gang_pursuit", "Gang Pursuit", MissionKind.GangPursuit, 4000, 800, 0, "pickup_a", "dropoff_c", "Several gang vehicles hunt you.", night: true, shadow: true, penalty: 1600),
                M(6, 2, "L6_double_rescue", "Double Rescue", MissionKind.DoubleRescue, 4300, 850, 420, "pickup_b", "dropoff_a", "Two people, one car, one chance.", night: true, shadow: true, penalty: 1700),
                M(6, 3, "L6_combined_pursuit", "Combined Pursuit", MissionKind.GangPursuit, 4600, 900, 0, "pickup_c", "dropoff_b", "Police and gang together.", night: true, shadow: true, penalty: 1800),
                // Level 7 - Professional Driver
                M(7, 1, "L7_vip_extraction", "VIP Extraction", MissionKind.Extraction, 5200, 1000, 480, "pickup_a", "dropoff_b", "Extract a VIP from a luxury building.", night: true, shadow: true, penalty: 2000),
                M(7, 2, "L7_dangerous_route", "Dangerous Route", MissionKind.Rescue, 5500, 1050, 0, "pickup_b", "dropoff_c", "The only route is through hostile territory.", night: true, shadow: true, penalty: 2100),
                M(7, 3, "L7_secret_warehouse", "Secret Warehouse", MissionKind.Rescue, 5800, 1100, 0, "pickup_c", "dropoff_a", "A hidden warehouse holds a target.", night: true, shadow: true, penalty: 2200),
                // Level 8 - Underground Legend
                M(8, 1, "L8_nightclub_extraction", "Nightclub Extraction", MissionKind.Extraction, 6500, 1200, 0, "pickup_a", "dropoff_c", "Pull a passenger out of the entertainment district.", night: true, shadow: true, penalty: 2500),
                M(8, 2, "L8_multiple_enemies", "Multiple Enemies", MissionKind.GangPursuit, 6800, 1250, 0, "pickup_b", "dropoff_a", "Enemies from several directions.", night: true, shadow: true, penalty: 2600),
                M(8, 3, "L8_premium_rescue", "Premium Rescue", MissionKind.Rescue, 7200, 1300, 480, "pickup_c", "dropoff_b", "High value, high risk.", night: true, shadow: true, penalty: 2800),
                // Level 9 - Double Agent
                M(9, 1, "L9_double_passenger_rescue", "Double Passenger Rescue", MissionKind.DoubleRescue, 8000, 1400, 0, "pickup_a", "dropoff_b", "Two passengers, two pickups.", night: true, shadow: true, penalty: 3200),
                M(9, 2, "L9_police_roadblock", "Police Roadblock", MissionKind.PoliceChase, 8500, 1450, 0, "pickup_b", "dropoff_c", "Roadblocks across every route.", night: true, shadow: true, penalty: 3400),
                M(9, 3, "L9_secret_extraction", "Secret Extraction", MissionKind.Extraction, 9000, 1500, 540, "pickup_c", "dropoff_a", "The quietest extraction of your career.", night: true, shadow: true, penalty: 3600),
                // Level 10 - Shadow Legend
                M(10, 1, "L10_final_pickup", "Final Pickup", MissionKind.Rescue, 10000, 1700, 0, "pickup_a", "dropoff_c", "The last passenger.", night: true, shadow: true, penalty: 4000),
                M(10, 2, "L10_gang_confrontation", "Gang Confrontation", MissionKind.Confrontation, 11000, 1800, 0, "pickup_b", "dropoff_a", "Face the gang that has hunted you.", night: true, shadow: true, penalty: 4500),
                M(10, 3, "L10_final_extraction", "Final Extraction", MissionKind.Extraction, 13000, 2000, 600, "pickup_c", "dropoff_b", "Everything leads here.", night: true, shadow: true, penalty: 5000),
            };
        }
    }
}
