using System;
using System.Linq;
using Comet.Unity;
using ShapeLand.Shared.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    public sealed partial class JoinScreen
    {
        // The server line's Change: the field in place of the line's text, for keys and gamepad too.
        private void OpenServerField()
        {
            _serverBefore = _server.value;
            _serverLine.AddToClassList("open");
            _serverField.RemoveFromClassList("hidden");
            _server.Focus();
        }

        private void CancelServerField()
        {
            _server.value = _serverBefore;
            CloseServerField();
        }

        private void CloseServerField()
        {
            _serverLine.RemoveFromClassList("open");
            _serverField.AddToClassList("hidden");
        }

        private void ShowServer()
        {
            if (_content != null)
            {
                _serverText.text = _content.Strings.Format("join.server_line", ("address", ServerAddress()));
            }
        }

        // The Login server in the card, or the default one when it's empty.
        private string ServerAddress() => string.IsNullOrWhiteSpace(_server.value) ? _join.DefaultLoginAddress : _server.value.Trim();

        // The Login server last used, else the default. In the editor, one on this computer means its local Login
        // server, on whatever port that uses now (Comet › Server); one elsewhere (a teammate's) stays as it was.
        private string RememberedServer()
        {
            var remembered = PlayerPrefs.GetString(ServerKey, "");
#if UNITY_EDITOR
            if (
                Comet.Unity.LocalServerPort.LoginInEditor != null
                && Uri.TryCreate(JoinClient.LoginUrl(remembered), UriKind.Absolute, out var url)
                && url.IsLoopback
            )
            {
                return _join.DefaultLoginAddress;
            }
#endif
            return string.IsNullOrWhiteSpace(remembered) ? _join.DefaultLoginAddress : remembered;
        }

#if UNITY_EDITOR
        /// <summary>The editor server's character file to come back from without accounts; null for its own, "" for none (tests).</summary>
        public static string DevCharactersFile { get; set; }

        // The editor's server's characters without accounts, as it keeps them (ShapeLand.GameServer's DevCharacters).
        [Serializable]
        private sealed class DevFile
        {
            public DevCharacter[] Characters = new DevCharacter[0];
            public DevStatus[] Statuses = new DevStatus[0];
        }

        [Serializable]
        private sealed class DevCharacter
        {
            public long Id;
            public long AccountId;
            public string Name = "";
            public int Shape,
                Color,
                EyeColor;
            public bool IsDeleted;
        }

        [Serializable]
        private sealed class DevStatus
        {
            public long CharacterId;
            public string UpdatedAt = "";
        }

        // The character last played on the editor's server (the latest status saved), or null for none or no file.
        // The editor server's account for people's characters (ShapeLand.GameServer's DevCharacters.AccountId).
        private const long PeopleAccount = 1;

        private static DevCharacter LastDevCharacter()
        {
            try
            {
                var path = DevCharactersFile ?? Comet.Unity.LocalServerPort.DevCharactersFile;
                if (path.Length == 0 || !System.IO.File.Exists(path))
                {
                    return null;
                }

                var file = JsonUtility.FromJson<DevFile>(System.IO.File.ReadAllText(path));
                // People's characters only: the editor's bots have their own account.
                var live = file?.Characters?.Where(c => !c.IsDeleted && c.Name.Length > 0 && c.AccountId == PeopleAccount).ToList();
                if (live == null || live.Count == 0)
                {
                    return null;
                }

                var latest = file
                    .Statuses?.Where(st => live.Any(c => c.Id == st.CharacterId))
                    .OrderByDescending(st =>
                        DateTime.TryParse(st.UpdatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : DateTime.MinValue
                    )
                    .FirstOrDefault();
                return latest != null ? live.First(c => c.Id == latest.CharacterId) : live[live.Count - 1];
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Couldn't read the editor server's characters ({e.Message}); starting with a new one.");
                return null;
            }
        }
#endif
    }
}
