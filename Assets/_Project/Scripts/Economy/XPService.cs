using System;
using CityDriver.Save;

namespace CityDriver.Economy
{
    /// <summary>
    /// Experience points. Separate from money and from the career level: XP only accumulates and drives the progress bar
    /// inside the current level band; the level itself advances through missions + vehicle requirements (CareerService).
    /// </summary>
    public sealed class XPService
    {
        // Cumulative XP at which each career level's band starts (index = level - 1).
        private static readonly long[] BandStart = { 0, 300, 800, 1500, 2400, 3500, 4800, 6300, 8000, 10000 };

        private readonly SaveData data;

        public XPService(SaveData data) { this.data = data ?? throw new ArgumentNullException(nameof(data)); }

        public event Action<long> XpChanged;

        public long Xp => data.xp;

        /// <summary>Cumulative XP at which the given career level's band starts.</summary>
        public static long BandStartXp(int level) => BandStart[Math.Max(0, Math.Min(level - 1, BandStart.Length - 1))];

        /// <summary>Sets the XP total directly (used when a failed Shadow Run drops the player a level).</summary>
        public void SetXp(long value) { data.xp = Math.Max(0, value); XpChanged?.Invoke(data.xp); }

        public void Add(long amount)
        {
            if (amount <= 0) return;
            data.xp += amount;
            XpChanged?.Invoke(data.xp);
        }

        /// <summary>0..1 progress through the band of the given career level (1 once the next band is reached).</summary>
        public float BandProgress01(int level)
        {
            int i = Math.Max(0, Math.Min(level - 1, BandStart.Length - 1));
            long start = BandStart[i];
            long end = i + 1 < BandStart.Length ? BandStart[i + 1] : start + 5000;
            return (float)Math.Max(0.0, Math.Min(1.0, (data.xp - start) / (double)(end - start)));
        }
    }
}
