using System;
using System.Collections;
using System.IO;
using Comet.Client;
using Comet.Protocol.Framing;
using Comet.Protocol.Messages;
using Comet.Unity;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;
using UnityEngine;
using UnityEngine.Networking;

namespace ShapeLand.Client
{
    /// <summary>
    /// Joins ShapeLand: loads the content, connects to a game server, joins as a random shape and logs what the
    /// server says (welcome, spawns, despawns, chat). On its own it's the bare join scene; the game scene draws
    /// on top of it.
    /// </summary>
    [RequireComponent(typeof(CometConnection))]
    public sealed class JoinClient : MonoBehaviour
    {
        public const string LogPrefix = "JOIN";

        [SerializeField] private string address = "localhost:5080";
        [SerializeField] private string playerName = "Player";

        private CometConnection _connection;
        private ShapeLandContent _content;
        private bool _asked;

        /// <summary>The server's host and port, or a full ws:// URL. Set before the component starts.</summary>
        public string Address
        {
            get => address;
            set => address = value;
        }

        public string PlayerName
        {
            get => playerName;
            set => playerName = value;
        }

        /// <summary>The loaded content, or null until it has loaded.</summary>
        public ShapeLandContent Content => _content;

        /// <summary>Raised once the content has loaded, before connecting.</summary>
        public event Action<ShapeLandContent> ContentLoaded;

        /// <summary>Raised when the server spawns this player (on joining, not on respawning).</summary>
        public event Action<PlayerSpawn> OwnSpawned;

        /// <summary>Raised when another player comes into view, after the session starts tracking them.</summary>
        public event Action<PlayerSpawn> OtherSpawned;

        /// <summary>Raised when another player leaves view.</summary>
        public event Action<uint> OtherLeft;

        /// <summary>True once the server has spawned this player.</summary>
        public bool Joined { get; private set; }

        public JoinRejection? Rejected { get; private set; }

        /// <summary>Why the client stopped: content that wouldn't load, or the connection's error.</summary>
        public string Error { get; private set; }

        public ClientSession Session => _connection.Session;

        private void Awake() => _connection = GetComponent<CometConnection>();

        private IEnumerator Start()
        {
            var path = Path.Combine(Application.streamingAssetsPath, "content.bin");
            var url = path.Contains("://") ? path : "file://" + path;
            using (var request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Fail($"Couldn't load {url}: {request.error}");
                    yield break;
                }

                _content = ShapeLandContent.Load(new MemoryStream(request.downloadHandler.data));
            }

            ContentLoaded?.Invoke(_content);

            var serverAddress = address;
#if UNITY_WEBGL && !UNITY_EDITOR
            serverAddress = AddressForPage(Application.absoluteURL, address);
#endif
            var serverUrl = new Uri(serverAddress.Contains("://") ? serverAddress : $"ws://{serverAddress}/ws");
            var session = _connection.Connect(serverUrl, ShapeLandProtocol.Options, ShapeLandWorld.TeleportSpeed(_content));
            session.WelcomeArrived += welcome => Log($"welcome: entity {welcome.EntityId}, {welcome.TickRate} ticks a second");
            session.GameMessage += OnGameMessage;
            session.EntityDespawned += id =>
            {
                Log($"entity {id} left");
                OtherLeft?.Invoke(id);
            };
            session.Corrected += correction => Log($"corrected ({correction.Reason}) to ({correction.X:0.0}, {correction.Y:0.0}, {correction.Z:0.0})");
            Log($"connecting to {serverUrl}");
        }

        /// <summary>
        /// The server a web build connects to: the one named by the page's <c>?server=</c> (a host and port, or a
        /// full ws:// URL), else the host the page came from. A page not served over HTTP uses <paramref name="fallback"/>.
        /// </summary>
        public static string AddressForPage(string pageUrl, string fallback)
        {
            if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var page) || (page.Scheme != "http" && page.Scheme != "https"))
            {
                return fallback;
            }

            foreach (var pair in page.Query.TrimStart('?').Split('&'))
            {
                if (pair.StartsWith("server=", StringComparison.Ordinal) && pair.Length > "server=".Length)
                {
                    return Uri.UnescapeDataString(pair.Substring("server=".Length));
                }
            }

            return $"{(page.Scheme == "https" ? "wss" : "ws")}://{page.Authority}/ws";
        }

        private void Update()
        {
            var session = _connection.Session;
            if (session == null || _content == null)
            {
                return;
            }

            if (session.Closed)
            {
                if (Error == null)
                {
                    Fail(session.Error ?? "The connection closed.");
                }

                return;
            }

            if (!_asked && session.Transport.State == TransportState.Open)
            {
                _asked = true;
                var shape = _content.Shapes.All[UnityEngine.Random.Range(0, _content.Shapes.All.Count)];
                // Colours, and a number after the default name so several clients can join, are random until
                // the join screen lets players pick them.
                if (playerName == "Player")
                {
                    playerName = $"Player {UnityEngine.Random.Range(100, 1000)}";
                }

                session.Write(ShapeLandMessageIds.JoinRequest, new JoinRequest
                {
                    Name = playerName,
                    Shape = shape.Number,
                    Colour = ShapeLandRules.BodyColours[UnityEngine.Random.Range(0, ShapeLandRules.BodyColours.Length)],
                    EyeColour = ShapeLandRules.EyeColours[UnityEngine.Random.Range(0, ShapeLandRules.EyeColours.Length)],
                });
                Log($"joining as {playerName}, a {shape.DisplayName}");
            }
        }

        private void OnGameMessage(ushort messageId, ReadOnlyMemory<byte> payload, uint tick)
        {
            var options = ShapeLandProtocol.Options;
            switch (messageId)
            {
                case ShapeLandMessageIds.PlayerSpawn:
                    var spawn = FrameReader.Decode<PlayerSpawn>(payload, options);
                    if (spawn.EntityId == Session.EntityId)
                    {
                        Joined = true;
                        Log($"spawned at ({spawn.X:0.0}, {spawn.Y:0.0}, {spawn.Z:0.0}), colour #{spawn.Colour:X6}, eyes #{spawn.EyeColour:X6}");
                        OwnSpawned?.Invoke(spawn);
                    }
                    else
                    {
                        Session.Entities.Spawn(spawn.EntityId, tick, new System.Numerics.Vector3(spawn.X, spawn.Y, spawn.Z), spawn.Facing);
                        Log($"{spawn.Name} (entity {spawn.EntityId}) is here");
                        OtherSpawned?.Invoke(spawn);
                    }

                    break;
                case ShapeLandMessageIds.JoinRejected:
                    Rejected = FrameReader.Decode<JoinRejected>(payload, options).Reason;
                    Log($"join rejected: {Rejected}");
                    break;
                case ShapeLandMessageIds.ChatMessage:
                    var chat = FrameReader.Decode<ChatMessage>(payload, options);
                    Log($"chat from entity {chat.EntityId}: {chat.Text}");
                    break;
            }
        }

        private void Fail(string error)
        {
            Error = error;
            Debug.LogWarning($"{LogPrefix}: {error}");
        }

        private static void Log(string line) => Debug.Log($"{LogPrefix}: {line}");
    }
}
