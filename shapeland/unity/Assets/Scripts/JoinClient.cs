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
    /// Joins ShapeLand: loads the content, connects to a game server, sends the join request and logs what the
    /// server says (welcome, spawns, despawns, chat). With <c>joinOnStart</c> it joins at once as a random shape
    /// (the bare join scene, tests); otherwise the join screen calls <see cref="Join"/> and can try again after a
    /// refusal or a failed connection. The game scene draws on top of it.
    /// </summary>
    [RequireComponent(typeof(CometConnection))]
    public sealed class JoinClient : MonoBehaviour
    {
        public const string LogPrefix = "JOIN";

        [SerializeField] private string address = "localhost:5080";
        [SerializeField] private string playerName = "Player";
        [SerializeField] private bool joinOnStart = true;

        private CometConnection _connection;
        private ShapeLandContent _content;
        private Uri _url;
        private JoinRequest? _pending;

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

        /// <summary>Whether to join as soon as the content loads, as a random shape and colours. Set before the component starts.</summary>
        public bool JoinOnStart
        {
            get => joinOnStart;
            set => joinOnStart = value;
        }

        /// <summary>
        /// The server to join unless the player names another: on the web, the one the page came from (or its
        /// <c>?server=</c>); elsewhere, <see cref="Address"/>.
        /// </summary>
        public string DefaultAddress
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return AddressForPage(Application.absoluteURL, address);
#else
                return address;
#endif
            }
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

        /// <summary>Raised when the server refuses the join; the player may try again.</summary>
        public event Action<JoinRejection> JoinRefused;

        /// <summary>Raised when the content won't load or the connection fails or closes, with the reason.</summary>
        public event Action<string> Failed;

        /// <summary>Raised for each chat line, the player's own included (the server echoes it, so everyone sees one order).</summary>
        public event Action<ChatMessage> ChatReceived;

        /// <summary>Raised when the server doesn't deliver this player's chat line.</summary>
        public event Action<ChatRejection> ChatRefused;

        /// <summary>True once the server has spawned this player.</summary>
        public bool Joined { get; private set; }

        /// <summary>The server's spawn of this player, once joined.</summary>
        public PlayerSpawn? Spawn { get; private set; }

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
            if (joinOnStart)
            {
                // A number after the default name lets several such clients join at once.
                var name = playerName == "Player" ? $"Player {UnityEngine.Random.Range(100, 1000)}" : playerName;
                Join(DefaultAddress, new JoinRequest
                {
                    Name = name,
                    Shape = _content.Shapes.All[UnityEngine.Random.Range(0, _content.Shapes.All.Count)].Number,
                    Colour = ShapeLandRules.BodyColours[UnityEngine.Random.Range(0, ShapeLandRules.BodyColours.Length)],
                    EyeColour = ShapeLandRules.EyeColours[UnityEngine.Random.Range(0, ShapeLandRules.EyeColours.Length)],
                });
            }
        }

        /// <summary>
        /// Asks to join <paramref name="serverAddress"/> (a host and port, or a full ws:// URL) once the content has
        /// loaded. Reuses the open connection to the same server, so a refused join can simply be tried again.
        /// </summary>
        public void Join(string serverAddress, JoinRequest request)
        {
            if (_content == null)
            {
                throw new InvalidOperationException("Join after the content has loaded.");
            }

            Error = null;
            Rejected = null;
            playerName = request.Name;
            if (!TryServerUrl(serverAddress, out var url))
            {
                Fail($"\"{serverAddress}\" isn't a server address. Use a host and port, like localhost:5080.");
                return;
            }

            var session = _connection.Session;
            if (session == null || session.Closed || url != _url)
            {
                Connect(url);
            }

            _pending = request;
        }

        /// <summary>Says <paramref name="text"/> in chat, once joined; the line arrives back through <see cref="ChatReceived"/>.</summary>
        public void Say(string text)
        {
            if (Joined && Session != null && !Session.Closed)
            {
                Session.Write(ShapeLandMessageIds.ChatSend, new ChatSend { Text = text });
            }
        }

        private void Connect(Uri serverUrl)
        {
            _url = serverUrl;
            var session = _connection.Connect(serverUrl, ShapeLandProtocol.Options, ShapeLandWorld.TeleportSpeed(_content),
                ShapeLandWorld.Rules.Gravity, ShapeLandWorld.GroundHeight(ShapeLandWorld.Create(_content)));
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
        /// The URL for a server address typed or given: a host and port (connected to with ws://), or a full ws:// or
        /// wss:// URL. False for anything else, such as a malformed host or port, or an http:// URL.
        /// </summary>
        public static bool TryServerUrl(string address, out Uri url)
        {
            address = (address ?? "").Trim();
            return Uri.TryCreate(address.Contains("://") ? address : $"ws://{address}/ws", UriKind.Absolute, out url)
                && (url.Scheme == "ws" || url.Scheme == "wss");
        }

        /// <summary>
        /// The server a web build connects to: the one named by the page's <c>?server=</c> (a host and port, or a
        /// full ws:// URL), else the host the page came from. A host and port is connected to securely (wss://) from
        /// a secure page, since browsers block plain connections from one. A page not served over HTTP uses
        /// <paramref name="fallback"/>.
        /// </summary>
        public static string AddressForPage(string pageUrl, string fallback)
        {
            if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var page) || (page.Scheme != "http" && page.Scheme != "https"))
            {
                return fallback;
            }

            var scheme = page.Scheme == "https" ? "wss" : "ws";
            foreach (var pair in page.Query.TrimStart('?').Split('&'))
            {
                if (pair.StartsWith("server=", StringComparison.Ordinal) && pair.Length > "server=".Length)
                {
                    var server = Uri.UnescapeDataString(pair.Substring("server=".Length));
                    return server.Contains("://") ? server : $"{scheme}://{server}/ws";
                }
            }

            return $"{scheme}://{page.Authority}/ws";
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
                _pending = null;
                if (Error == null)
                {
                    Fail(session.Error ?? "The connection closed.");
                }

                return;
            }

            if (_pending is JoinRequest request && session.Transport.State == TransportState.Open)
            {
                _pending = null;
                session.Write(ShapeLandMessageIds.JoinRequest, request);
                var shape = _content.Shapes.TryGet(request.Shape, out var found) ? found.DisplayName : $"shape {request.Shape}";
                Log($"joining as {request.Name}, a {shape}");
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
                        Spawn = spawn;
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
                    var reason = FrameReader.Decode<JoinRejected>(payload, options).Reason;
                    Rejected = reason;
                    Log($"join rejected: {reason}");
                    JoinRefused?.Invoke(reason);
                    break;
                case ShapeLandMessageIds.ChatMessage:
                    var chat = FrameReader.Decode<ChatMessage>(payload, options);
                    Log($"chat from entity {chat.EntityId}: {chat.Text}");
                    ChatReceived?.Invoke(chat);
                    break;
                case ShapeLandMessageIds.ChatRejected:
                    var refusal = FrameReader.Decode<ChatRejected>(payload, options).Reason;
                    Log($"chat rejected: {refusal}");
                    ChatRefused?.Invoke(refusal);
                    break;
            }
        }

        private void Fail(string error)
        {
            Error = error;
            Debug.LogWarning($"{LogPrefix}: {error}");
            Failed?.Invoke(error);
        }

        private static void Log(string line) => Debug.Log($"{LogPrefix}: {line}");
    }
}
