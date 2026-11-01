using System;
using Comet.Protocol.Login;
using ShapeLand.Shared.Messages;
using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>
    /// When to remind a guest, once, to set a password: on their second visit, the first time they come back on a
    /// later day. Kept in PlayerPrefs, so per browser (or device), like the guest's character itself.
    /// </summary>
    public static class GuestReminder
    {
        /// <summary>How long the reminder stays up.</summary>
        public const float ShowSeconds = 15;

        private const string FirstDayKey = "guest-first-day";
        private const string ShownKey = "guest-reminded";

        /// <summary>Whether to show the reminder now (and if so, notes that it was shown).</summary>
        public static bool Due(AccountResponse<CharacterView> account) => Due(account, DateTime.Today);

        /// <summary>The same, on <paramref name="today"/>, for tests.</summary>
        public static bool Due(AccountResponse<CharacterView> account, DateTime today)
        {
            if (account == null || !account.Guest || PlayerPrefs.GetInt(ShownKey, 0) == 1)
            {
                return false;
            }

            var day = (int)(today - DateTime.UnixEpoch.Date).TotalDays;
            var first = PlayerPrefs.GetInt(FirstDayKey, -1);
            if (first < 0)
            {
                PlayerPrefs.SetInt(FirstDayKey, day);
                PlayerPrefs.Save();
                return false;
            }

            if (day <= first)
            {
                return false;
            }

            PlayerPrefs.SetInt(ShownKey, 1);
            PlayerPrefs.Save();
            return true;
        }
    }
}
