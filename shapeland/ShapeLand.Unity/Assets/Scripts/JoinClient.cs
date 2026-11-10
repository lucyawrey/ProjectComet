using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Comet.Client;
using Comet.Client.Login;
using Comet.Content;
using Comet.Protocol.Framing;
using Comet.Protocol.Login;
using Comet.Simulation;
using Comet.Unity;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;
using UnityEngine;
using UnityEngine.Networking;

namespace ShapeLand.Client
{
    [RequireComponent(typeof(CometConnection))]
    public sealed partial class JoinClient : MonoBehaviour
    {
        private const string LogPrefix = "JOIN";

        [SerializeField]
        private string address = "localhost:5090";

        private CometConnection _connection;
        private ShapeLandContent _content;
        private LoginClient<CharacterView> _login;
        private string _loginAddress;
        private Uri _url;
        private JoinRequest? _pending;
        private DevJoinRequest? _pendingDev;
        private string _devName = "";

        private int _joinAttempt;
        private bool _ticketRetried;
        private bool _awaitingAnswer;
        private readonly HashSet<string> _flipped = new HashSet<string>();
        private double _askedAt;
        private double _connectedAt;

        public string PlayerName => Accounts ? Account?.Username ?? "" : _devName;

        public static bool Accounts => Comet.Unity.LocalServerPort.UseAccounts;

        public string DefaultLoginAddress
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return LoginForPage(Application.absoluteURL, address, ShapeLand_PageLogin());
#else
                return Comet.Unity.LocalServerPort.LoginInEditor is { } port ? $"localhost:{port}" : address;
#endif
            }
        }

        public AccountResponse<CharacterView> Account
        {
            get => _account;
            private set
            {
                _account = value;
                _content?.Strings.SetShared("username", value?.Username ?? "");
            }
        }

        private AccountResponse<CharacterView> _account;

        public bool SignedIn => Accounts && (_login?.SignedIn ?? false);

        public DropReason? DroppedFor { get; private set; }

        public ShapeLandContent Content => _content;

        public event Action<ShapeLandContent> ContentLoaded;

        public event Action<PlayerSpawn> OwnSpawned;

        public event Action<PlayerSpawn> OtherSpawned;

        public event Action<uint> OtherLeft;

        public event Action<JoinRejection> JoinRefused;

        public event Action<LookRejection> LookRefused;

        public event Action SignedOut;

        public event Action<CloseReason> ConnectionLost;

        public event Action<JoinFailure> Failed;

        public event Action<string> RetryFailed;

        public event Action<ChatMessage> ChatReceived;

        public event Action<ChatRejection> ChatRefused;

        public event Action<Zone> ZoneChanged;

        public event Action<string, bool> PlacementsFlipped;

        public Zone Zone { get; private set; }

        public CollisionWorld World { get; private set; }

        public IReadOnlyCollection<string> Flipped => _flipped;

        public event Action<int, bool> SpawnSet;

        public int? SpawnNumber { get; private set; }

        public bool Joined { get; private set; }

        public PlayerSpawn? Spawn { get; private set; }

        public JoinRejection? Rejected { get; private set; }

        public JoinFailure Failure { get; private set; }

        public string Error { get; private set; }

        public ClientSession Session => _connection.Session;

        private void Awake()
        {
            _connection = GetComponent<CometConnection>();
            UseLoginServer(DefaultLoginAddress);
        }

        public void CancelJoin()
        {
            _joinAttempt++;
            if (Joined)
            {
                return;
            }

            _pending = null;
            _pendingDev = null;
            _awaitingAnswer = false;
            _connection.Disconnect();
        }

        public async Task<string> PlayAsync()
        {
            if (_content == null)
            {
                throw new InvalidOperationException("Join after the content has loaded.");
            }

            Failure = JoinFailure.None;
            Error = null;
            Rejected = null;
            DroppedFor = null;
            var attempt = _joinAttempt;
            var ticket = await _login.GetTicketAsync();
            if (attempt != _joinAttempt)
            {
                return null;
            }
            if (!ticket.Succeeded)
            {
                if (ticket.Error == LoginErrors.SignedOut)
                {
                    Account = null;
                    SignedOut?.Invoke();
                }

                return ticket.Error;
            }

            if (!TryServerUrl(ticket.Value.Server, out var url))
            {
                Fail(JoinFailure.BadAddress, $"The Login server named \"{ticket.Value.Server}\", which isn't a game server address.");
                return null;
            }

            Connect(url);
            _pending = new JoinRequest { Ticket = ticket.Value.Ticket };
            return null;
        }

        public void DevJoin(string name, NewCharacter look)
        {
            if (_content == null)
            {
                throw new InvalidOperationException("Join after the content has loaded.");
            }

            Failure = JoinFailure.None;
            Error = null;
            Rejected = null;
            DroppedFor = null;
            _devName = name;
            _content.Strings.SetShared("username", name);
            Connect(new Uri($"ws://localhost:{Comet.Unity.LocalServerPort.InEditor ?? 5080}/ws"));
            _pendingDev = new DevJoinRequest
            {
                Name = name,
                Shape = look.Shape,
                Color = look.Color,
                EyeColor = look.EyeColor,
            };
        }

        private async void RetryAsync()
        {
            var error = await PlayAsync();
            if (error != null && error != LoginErrors.SignedOut)
            {
                RetryFailed?.Invoke(error);
            }
        }

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
                _content.Strings.SetVariant(Application.platform == RuntimePlatform.WebGLPlayer ? "web" : null);
            }

            Zone = ShapeLandWorld.StartOf(_content, _content.Shapes.All[0]).Zone;
            World = ShapeLandWorld.Create(_content, Zone);

            ContentLoaded?.Invoke(_content);
        }

        public void ChangeLook(LookChange change)
        {
            if (!Joined)
            {
                throw new InvalidOperationException("Change the look once joined.");
            }

            Session.Write(ShapeLandMessageIds.LookChange, change);
        }

        public void Leave()
        {
            if (Joined)
            {
                Log("left the game");
                EndSession(CloseReason.None);
            }
        }

        private void EndSession(CloseReason reason)
        {
            Joined = false;
            Spawn = null;
            _pending = null;
            _pendingDev = null;
            _awaitingAnswer = false;
            _connection.Disconnect();
            ConnectionLost?.Invoke(reason);
        }

        public void ReturnToSpawn()
        {
            if (Joined)
            {
                Session.Write(ShapeLandMessageIds.ReturnToSpawn, new ReturnToSpawn());
            }
        }

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
            var session = _connection.Connect(
                serverUrl,
                ShapeLandProtocol.Options,
                ShapeLandWorld.TeleportSpeed(_content),
                _content.Movement.Gravity,
                World.GroundHeight(_content.Movement)
            );
            session.WelcomeArrived += welcome => Log($"welcome: entity {welcome.EntityId}, {welcome.TickRate} ticks a second");
            session.GameMessage += OnGameMessage;
            session.EntityDespawned += id =>
            {
                Log($"entity {id} left");
                OtherLeft?.Invoke(id);
            };
            session.Corrected += correction =>
                Log($"corrected ({correction.Reason}) to ({correction.X:0.0}, {correction.Y:0.0}, {correction.Z:0.0})");
            Log($"connecting to {serverUrl}");
        }

        public static bool TryServerUrl(string address, out Uri url)
        {
            address = (address ?? "").Trim();
            return Uri.TryCreate(address.Contains("://") ? address : $"ws://{address}/ws", UriKind.Absolute, out url)
                && (url.Scheme == "ws" || url.Scheme == "wss");
        }

        private void Update()
        {
            var session = _connection.Session;
            if (session == null || _content == null)
            {
                return;
            }

            if (session.Closed && Joined)
            {
                Log($"connection lost: {session.CloseReason} ({session.Error})");
                EndSession(session.CloseReason);
                return;
            }

            if (session.Closed)
            {
                var idle = !Joined && _pending == null && _pendingDev == null && !_awaitingAnswer;
                _pending = null;
                _pendingDev = null;
                _awaitingAnswer = false;
                if (Failure == JoinFailure.None && !idle)
                {
                    Fail(JoinFailure.Unreachable, $"{session.CloseReason}: {session.Error}");
                }

                return;
            }

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
                Log($"joining as {PlayerName}");
            }

            if (_pendingDev is DevJoinRequest devRequest && session.Transport.State == TransportState.Open)
            {
                _pendingDev = null;
                _awaitingAnswer = true;
                _askedAt = Time.realtimeSinceStartupAsDouble;
                devRequest.ContentFingerprint = _content.Fingerprint;
                session.Write(ShapeLandMessageIds.DevJoinRequest, devRequest);
                Log($"joining as {PlayerName}, without accounts");
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
                        _ticketRetried = false;
                        Spawn = spawn;
                        if (!Accounts)
                        {
                            _devName = spawn.Name;
                            _content.Strings.SetShared("username", spawn.Name);
                        }

                        Log($"spawned at ({spawn.X:0.0}, {spawn.Y:0.0}, {spawn.Z:0.0}) as {spawn.Name}, color {spawn.Color}, eyes {spawn.EyeColor}");
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
                    _awaitingAnswer = false;
                    Log($"join rejected: {reason}");
                    if ((reason == JoinRejection.ExpiredTicket || reason == JoinRejection.UsedTicket) && !_ticketRetried)
                    {
                        _ticketRetried = true;
                        RetryAsync();
                        break;
                    }

                    _ticketRetried = false;
                    Rejected = reason;
                    _connection.Disconnect();
                    JoinRefused?.Invoke(reason);
                    break;
                case ShapeLandMessageIds.LookRejected:
                    var lookRefusal = FrameReader.Decode<LookRejected>(payload, options).Reason;
                    Log($"look change rejected: {lookRefusal}");
                    LookRefused?.Invoke(lookRefusal);
                    break;
                case ShapeLandMessageIds.Dropped:
                    // The connection closes next; the client doesn't rejoin by itself, so two tabs can't keep taking the character from each other.
                    DroppedFor = FrameReader.Decode<Dropped>(payload, options).Reason;
                    Log($"dropped: {DroppedFor}");
                    break;
                case ShapeLandMessageIds.ChatMessage:
                    var chat = FrameReader.Decode<ChatMessage>(payload, options);
                    Log($"chat from entity {chat.EntityId}: {chat.Text}");
                    ChatReceived?.Invoke(chat);
                    break;
                case ShapeLandMessageIds.SpawnSet:
                    var set = FrameReader.Decode<SpawnSet>(payload, options);
                    SpawnNumber = set.Spawn;
                    Log($"spawn point is now {SpawnNumber}{(set.Claimed ? ", claimed" : "")}");
                    SpawnSet?.Invoke(set.Spawn, set.Claimed);
                    break;
                case ShapeLandMessageIds.ZoneEntered:
                    var entered = FrameReader.Decode<ZoneEntered>(payload, options);
                    if (!_content.Zones.TryGet(entered.Zone, out var zone))
                    {
                        Debug.LogWarning($"{LogPrefix}: the server put the player in zone {entered.Zone}, which the content doesn't have.");
                        break;
                    }

                    // The world is built here, in the same frame before the arrival correction, so the player's movement never runs on the old zone.
                    EnterZone(zone, rebuild: true);
                    Session.Entities.GroundHeight = World.GroundHeight(_content.Movement);
                    foreach (var tag in entered.Flipped ?? Array.Empty<string>())
                    {
                        Flip(tag, true);
                    }

                    break;
                case ShapeLandMessageIds.PlacementsFlipped:
                    var flipped = FrameReader.Decode<PlacementsFlipped>(payload, options);
                    Flip(flipped.Tag, flipped.Flipped);
                    break;
                case ShapeLandMessageIds.ChatRejected:
                    var refusal = FrameReader.Decode<ChatRejected>(payload, options).Reason;
                    Log($"chat rejected: {refusal}");
                    ChatRefused?.Invoke(refusal);
                    break;
            }
        }

        public void ShowZone(Zone zone)
        {
            if (_content != null && !Joined)
            {
                EnterZone(zone, rebuild: false);
            }
        }

        private void EnterZone(Zone zone, bool rebuild)
        {
            if (zone.Number == Zone.Number && !rebuild)
            {
                return;
            }

            Zone = zone;
            World = ShapeLandWorld.Create(_content, zone);
            _flipped.Clear();
            Log($"in {zone.Id}");
            ZoneChanged?.Invoke(zone);
        }

        private void Flip(string tag, bool flipped)
        {
            World.SetFlipped(tag, flipped);
            if (flipped)
            {
                _flipped.Add(tag);
            }
            else
            {
                _flipped.Remove(tag);
            }

            Log($"{tag} {(flipped ? "flipped" : "back")}");
            PlacementsFlipped?.Invoke(tag, flipped);
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

    public enum JoinFailure
    {
        None,

        NoContent,

        BadAddress,

        Unreachable,

        Unanswered,
    }
}
