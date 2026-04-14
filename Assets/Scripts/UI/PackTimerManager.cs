// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Pack Timer Manager
//  Enforces a 12-hour cooldown between booster pack pulls.
//  Uses PlayerPrefs to persist across sessions.
// ═══════════════════════════════════════════════════════

using System;
using UnityEngine;

namespace DualCraft.UI
{
    public class PackTimerManager
    {
        private const string LastPullKey = "LastPackPullTime";
        private const double CooldownHours = 12.0;

        /// <summary>Returns true if the player can open a pack right now.</summary>
        public static bool CanOpenPack()
        {
            string last = PlayerPrefs.GetString(LastPullKey, "");
            if (string.IsNullOrEmpty(last)) return true;

            if (DateTime.TryParse(last, out DateTime lastPull))
            {
                return (DateTime.UtcNow - lastPull).TotalHours >= CooldownHours;
            }
            return true;
        }

        /// <summary>Record that a pack was just opened.</summary>
        public static void RecordPull()
        {
            PlayerPrefs.SetString(LastPullKey, DateTime.UtcNow.ToString("O"));
            PlayerPrefs.Save();
        }

        /// <summary>Returns remaining cooldown as a formatted string, or empty if ready.</summary>
        public static string GetTimeRemaining()
        {
            string last = PlayerPrefs.GetString(LastPullKey, "");
            if (string.IsNullOrEmpty(last)) return "";

            if (DateTime.TryParse(last, out DateTime lastPull))
            {
                double hoursLeft = CooldownHours - (DateTime.UtcNow - lastPull).TotalHours;
                if (hoursLeft <= 0) return "";

                int h = (int)hoursLeft;
                int m = (int)((hoursLeft - h) * 60);
                return $"{h}h {m}m";
            }
            return "";
        }

        /// <summary>Returns remaining seconds (for UI countdown).</summary>
        public static float GetSecondsRemaining()
        {
            string last = PlayerPrefs.GetString(LastPullKey, "");
            if (string.IsNullOrEmpty(last)) return 0f;

            if (DateTime.TryParse(last, out DateTime lastPull))
            {
                double secondsLeft = (CooldownHours * 3600) - (DateTime.UtcNow - lastPull).TotalSeconds;
                return Mathf.Max(0f, (float)secondsLeft);
            }
            return 0f;
        }
    }
}
