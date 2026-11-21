using CityDriver.Save;

namespace CityDriver.Career
{
    public enum MissionKind
    {
        Taxi, TimeChallenge, Vip, LongJourney, NightPassenger, Mysterious,
        Rescue, GangEscape, PoliceChase, EmergencyPickup, HighwayEscape, GangPursuit, DoubleRescue, Extraction, Confrontation
    }

    /// <summary>
    /// One assignment. Plain data (no Unity references) so it is testable and can later be loaded from ScriptableObjects or JSON.
    /// Locations are LocationMarker ids; they are rebound to the real city when it is rebuilt.
    /// </summary>
    public sealed class MissionDefinition
    {
        public string Id;
        public int Level;
        public int Order;                 // 1..3 inside the level
        public string Title;
        public string Briefing;
        public MissionKind Kind;
        public long Reward;               // rupees on first completion
        public long Xp;
        public int TimeLimitSeconds;      // 0 = untimed
        public string PickupId;
        public string DropoffId;
        public bool NightOnly;
        public bool ShadowRun;            // secret rescue mode: needs Shadow Pass, has a wallet penalty on failure
        public long FailPenalty;          // applied exactly once per failed attempt (Shadow Run only)
        public string CompletionCriteria;
        public string FailureCriteria;

        public override string ToString() => $"L{Level}.{Order} {Title}";
    }

    /// <summary>A mandatory vehicle upgrade needed before a level can be completed.</summary>
    public readonly struct UpgradeRequirement
    {
        public readonly UpgradeType Type;
        public readonly int MinLevel;
        public UpgradeRequirement(UpgradeType type, int minLevel) { Type = type; MinLevel = minLevel; }
    }
}
