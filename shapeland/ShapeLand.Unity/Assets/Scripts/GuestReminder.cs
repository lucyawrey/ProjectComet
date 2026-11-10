using System;
using Comet.Protocol.Login;
using ShapeLand.Shared.Messages;
using UnityEngine;

namespace ShapeLand.Client
{
    public static class GuestReminder
    {
        public const float ShowSeconds = 15;

        private const string FirstDayKey = "guest-first-day";
        private const string ShownKey = "guest-reminded";

        public static bool Due(AccountResponse<CharacterView> account) => Due(account, DateTime.Today);

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
