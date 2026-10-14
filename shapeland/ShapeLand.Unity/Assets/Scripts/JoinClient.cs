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
    /// server says (welcome, spawns, despawns, chat). The join screen calls <see cref="Join"/> and can try again
    /// after a refusal or a failed connection; the rest of the game scene draws on top of it.
    /// </summary>
    [RequireComponent(typeof(CometConnection))]
    public sealed class JoinClient : MonoBehaviour
    {
        private const string LogPrefix = "JOIN";

        [SerializeField] private string address = "localhost:5080";

        private CometConnection _connection;
        private ShapeLandContent _content;
        private Uri _url;
        private JoinRequest? _pending;
        private bool _awaitingAnswer;
        private double _askedAt;
        private double _connectedAt;

        /// <summary>The name last asked for.</summary>
        public string PlayerName { get; private set; }

        /// <summary>
        /// The server to join unless the player names another: on the web, the page's <c>?server=</c>, else the one its
        /// deploy names (<c>config.js</c>), else the one the page came from; elsewhere, <see cref="Address"/>.
        /// </summary>
        public string DefaultAddress
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return AddressForPage(Application.absoluteURL, address, ShapeLand_PageServer());
#else
                return address;
#endif
            }
        }

        /// <summary>The loaded content, or null until it has loaded.</summary>
        public ShapeLandContent Content => _content;

        /// <summary>Raised once the content has loaded, before connecting.</summary>
        public event Action<ShapeLandContent> ContentLoaded;

        /// <summary>Raised when the server spawns this player: on joining, and again with a changed look (not on respawning).</summary>
        public event Action<PlayerSpawn> OwnSpawned;

        /// <summary>Raised when another player comes into view, after the session starts tracking them.</summary>
        public event Action<PlayerSpawn> OtherSpawned;

        /// <summary>Raised when another player leaves view.</summary>
        public event Action<uint> OtherLeft;

        /// <summary>Raised when the server refuses the join or a change of look; the player may try again.</summary>
        public event Action<JoinRejection> JoinRefused;

        /// <summary>
        /// The connection was lost while joined (the server closed it, stopped answering, or the game fell too far
        /// behind), with the reason. The player is gone from the world; the next <see cref="Join"/> starts over.
        /// </summary>
        public event Action<CloseReason> ConnectionLost;

        /// <summary>Raised when the content won't load, or joining fails before the player is in; <see cref="Error"/> has the details.</summary>
        public event Action<JoinFailure> Failed;

        /// <summary>Raised for each chat line, the player's own included (the server echoes it, so everyone sees one order).</summary>
        public event Action<ChatMessage> ChatReceived;

        /// <summary>Raised when the server doesn't deliver this player's chat line.</summary>
        public event Action<ChatRejection> ChatRefused;

        /// <summary>True once the server has spawned this player.</summary>
        public bool Joined { get; private set; }

        /// <summary>The server's spawn of this player, once joined.</summary>
        public PlayerSpawn? Spawn { get; private set; }

        /// <summary>Why the server turned the last join down, or null.</summary>
        public JoinRejection? Rejected { get; private set; }

        /// <summary>The last failure, or <see cref="JoinFailure.None"/>.</summary>
        public JoinFailure Failure { get; private set; }

        /// <summary>The details of <see cref="Failure"/>, in English, for logs; null when there's none.</summary>
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
                    Fail(JoinFailure.NoContent, $"Couldn't load {url}: {request.error}");
                    yield break;
                }

                _content = ShapeLandContent.Load(new MemoryStream(request.downloadHandler.data));
            }

            ContentLoaded?.Invoke(_content);
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

            Failure = JoinFailure.None;
            Error = null;
            Rejected = null;
            PlayerName = request.Name;
            if (!TryServerUrl(serverAddress, out var url))
            {
                Fail(JoinFailure.BadAddress, $"\"{serverAddress}\" isn't a server address.");
                return;
            }

            // A connection kept from a refused join is reused, unless the server is about to drop it for not joining.
            var session = _connection.Session;
            var nearDeadline = !Joined && Time.realtimeSinceStartupAsDouble - _connectedAt > ShapeLandRules.JoinDeadlineSeconds - 3;
            if (session == null || session.Closed || url != _url || nearDeadline)
            {
                Connect(url);
            }

            _pending = request;
        }

        /// <summary>
        /// Once joined, asks to change the name, shape or colors where the player stands. The answer is a new
        /// <see cref="OwnSpawned"/>, or <see cref="JoinRefused"/>.
        /// </summary>
        public void Change(JoinRequest request)
        {
            if (!Joined)
            {
                throw new InvalidOperationException("Change the look once joined.");
            }

            Rejected = null;
            PlayerName = request.Name;
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
            _connectedAt = Time.realtimeSinceStartupAsDouble;
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
        /// full ws:// URL), else <paramref name="pageServer"/> (the server the page's deploy names, when it isn't the
        /// page's own host), else the host the page came from. A host and port is connected to securely (wss://) from
        /// a secure page, since browsers block plain connections from one. A page not served over HTTP uses
        /// <paramref name="fallback"/>.
        /// </summary>
        public static string AddressForPage(string pageUrl, string fallback, string pageServer = null)
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

            return string.IsNullOrEmpty(pageServer) ? $"{scheme}://{page.Authority}/ws" : pageServer;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // Plugins/WebGL/ShapeLandPage.jslib.
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern string ShapeLand_PageServer();
#endif

        private void Update()
        {
            var session = _connection.Session;
            if (session == null || _content == null)
            {
                return;
            }

            if (session.Closed && Joined)
            {
                var reason = session.CloseReason;
                Joined = false;
                Spawn = null;
                _pending = null;
                _awaitingAnswer = false;
                _connection.Disconnect(); // the next Join connects afresh
                Log($"connection lost: {reason} ({session.Error})");
                ConnectionLost?.Invoke(reason);
                return;
            }

            if (session.Closed)
            {
                // Idle after a refused join, the server drops the connection after its join deadline; that's no
                // failure, and the next Join reconnects.
                var idle = !Joined && _pending == null && !_awaitingAnswer;
                _pending = null;
                _awaitingAnswer = false;
                if (Failure == JoinFailure.None && !idle)
                {
                    Fail(JoinFailure.Unreachable, $"{session.CloseReason}: {session.Error}");
                }

                return;
            }

            // Pings only start with the welcome, so a server that goes quiet before answering the join needs its own limit.
            if (_awaitingAnswer && Time.realtimeSinceStartupAsDouble - _askedAt > TransportLimits.ConnectTimeoutSeconds)
            {
                _awaitingAnswer = false;
                _connection.Disconnect();
                Fail(JoinFailure.Unanswered, "The server didn't answer the request to join.");
                return;
            }

            if (_pending is JoinRequest request && session.Transport.State == TransportState.Open)
            {
                _pending = null;
                _awaitingAnswer = true;
                _askedAt = Time.realtimeSinceStartupAsDouble;
                request.ContentFingerprint = _content.Fingerprint;
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
                        _awaitingAnswer = false;
                        Spawn = spawn;
                        Log($"spawned at ({spawn.X:0.0}, {spawn.Y:0.0}, {spawn.Z:0.0}), color #{spawn.Color:X6}, eyes #{spawn.EyeColor:X6}");
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
                    _awaitingAnswer = false;
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

        private void Fail(JoinFailure failure, string error)
        {
            Failure = failure;
            Error = error;
            Debug.LogWarning($"{LogPrefix}: {error}");
            Failed?.Invoke(failure);
        }

        private static void Log(string line) => Debug.Log($"{LogPrefix}: {line}");
    }

    /// <summary>Why the join client stopped before the player was in.</summary>
    public enum JoinFailure
    {
        None,

        /// <summary>The content file wouldn't load.</summary>
        NoContent,

        /// <summary>The server address given isn't one.</summary>
        BadAddress,

        /// <summary>The connection failed or closed before the player was in.</summary>
        Unreachable,

        /// <summary>The server didn't answer the join request in time.</summary>
        Unanswered,
    }
}
