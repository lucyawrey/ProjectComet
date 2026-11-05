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
using Comet.Protocol.Messages;
using Comet.Simulation;
using Comet.Unity;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;
using UnityEngine;
using UnityEngine.Networking;

namespace ShapeLand.Client
{
    /// <summary>
    /// Joins ShapeLand: loads the content, signs in at the Login server (keeping the session token in PlayerPrefs),
    /// gets a join ticket, connects to the game server the ticket is for, and logs what the server says (welcome,
    /// zones, spawns, despawns, flips, chat). A returning player resumes straight into the game
    /// (<see cref="ResumeAsync"/>); a new one makes a guest account from the join card. The rest of the game scene draws
    /// on top of it. Holds the zone being shown: before joining, the picked shape's start zone (<see cref="ShowZone"/>);
    /// once joined, the one the server has put the player in.
    /// </summary>
    [RequireComponent(typeof(CometConnection))]
    public sealed class JoinClient : MonoBehaviour
    {
        private const string LogPrefix = "JOIN";

        [SerializeField]
        private string address = "localhost:5090";

        /// <summary>
        /// The start of the session tokens' PlayerPrefs keys, one per Login server, so a token is only ever sent to the
        /// server it came from. Tests use their own, so they never touch a player's.
        /// </summary>
        public static string TokenKey = "login-token";

        /// <summary>Forgets the token kept for a Login server (tests, so each starts as a new player and leaves nothing behind).</summary>
        public static void ForgetToken(string loginAddress) => PlayerPrefs.DeleteKey(TokenKeyFor(LoginUrl(loginAddress)));

        private static string TokenKeyFor(string loginUrl) => $"{TokenKey} {loginUrl}";

        private CometConnection _connection;
        private ShapeLandContent _content;
        private LoginClient<CharacterView> _login;
        private string _loginAddress;
        private Uri _url;
        private JoinRequest? _pending;
        private DevJoinRequest? _pendingDev;
        private string _devName = "";

        // Bumped by CancelJoin, so a join already on its way stops before connecting.
        private int _joinAttempt;
        private bool _ticketRetried;
        private bool _awaitingAnswer;
        private readonly HashSet<string> _flipped = new HashSet<string>();
        private double _askedAt;
        private double _connectedAt;

        /// <summary>The player's name: their account's username, or with accounts off, the name they joined with.</summary>
        public string PlayerName => Accounts ? Account?.Username ?? "" : _devName;

        /// <summary>
        /// Whether joining goes through a Login server: always in builds; in the editor, unless Comet › Server › Use
        /// Accounts is off, when play mode joins the editor's own server by name (<see cref="DevJoin"/>).
        /// </summary>
        public static bool Accounts => Comet.Unity.LocalServerPort.UseAccounts;

        /// <summary>
        /// The Login server to use unless the player names another: on the web, the page's <c>?login=</c>, else the one
        /// its deploy names (<c>config.js</c>); in the editor, its local Login server on this computer (Comet › Server);
        /// elsewhere, the serialized address.
        /// </summary>
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

        /// <summary>The signed-in account and its character, once loaded; null when signed out.</summary>
        public AccountResponse<CharacterView> Account
        {
            get => _account;
            private set
            {
                _account = value;
                _content?.Strings.SetShared("username", value?.Username ?? ""); // every {username} in the interface text
            }
        }

        private AccountResponse<CharacterView> _account;

        /// <summary>A session token is kept: the player has an account on this device (never with accounts off).</summary>
        public bool SignedIn => Accounts && (_login?.SignedIn ?? false);

        /// <summary>Why the server dropped this player (another tab or device took over, or the account was removed), or null.</summary>
        public DropReason? DroppedFor { get; private set; }

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

        /// <summary>Raised when the server refuses the join; the player may try again.</summary>
        public event Action<JoinRejection> JoinRefused;

        /// <summary>Raised when the server refuses a change of look; the old look stays.</summary>
        public event Action<LookRejection> LookRefused;

        /// <summary>Raised when the Login server refused the session token (expired, revoked, or a deleted guest): show the join card.</summary>
        public event Action SignedOut;

        /// <summary>
        /// The connection was lost while joined (the server closed it, stopped answering, or the game fell too far
        /// behind), with the reason, or the player left (<see cref="Leave"/>, reason <see cref="CloseReason.None"/>).
        /// The player is gone from the world; the next <see cref="Join"/> starts over.
        /// </summary>
        public event Action<CloseReason> ConnectionLost;

        /// <summary>Raised when the content won't load, or joining fails before the player is in; <see cref="Error"/> has the details.</summary>
        public event Action<JoinFailure> Failed;

        /// <summary>
        /// Raised when the join's own retry with a fresh ticket couldn't get one, with the Login server's error code
        /// (a signed-out session raises <see cref="SignedOut"/> instead).
        /// </summary>
        public event Action<string> RetryFailed;

        /// <summary>Raised for each chat line, the player's own included (the server echoes it, so everyone sees one order).</summary>
        public event Action<ChatMessage> ChatReceived;

        /// <summary>Raised when the server doesn't deliver this player's chat line.</summary>
        public event Action<ChatRejection> ChatRefused;

        /// <summary>
        /// The zone shown changed (another shape picked before joining, or the server put the player in another
        /// zone); <see cref="Zone"/> and <see cref="World"/> are now its, with nothing flipped yet.
        /// </summary>
        public event Action<Zone> ZoneChanged;

        /// <summary>
        /// Triggers flipped the placements with a tag from how they start (true), or back (false); <see cref="World"/>
        /// already has them so. Also raised for each flipped tag on entering a zone.
        /// </summary>
        public event Action<string, bool> PlacementsFlipped;

        /// <summary>The zone shown, once the content has loaded.</summary>
        public Zone Zone { get; private set; }

        /// <summary>The collision world of <see cref="Zone"/>, with the placements triggers have flipped.</summary>
        public CollisionWorld World { get; private set; }

        /// <summary>The names of <see cref="Zone"/>'s placements flipped from how they start.</summary>
        public IReadOnlyCollection<string> Flipped => _flipped;

        /// <summary>
        /// The player's spawn point changed, by content number: on joining (false), and on claiming another by
        /// standing in it (true).
        /// </summary>
        public event Action<int, bool> SpawnSet;

        /// <summary>The player's spawn point's content number, once the server has said.</summary>
        public int? SpawnNumber { get; private set; }

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

        private void Awake()
        {
            _connection = GetComponent<CometConnection>();
            UseLoginServer(DefaultLoginAddress);
        }

        /// <summary>Uses another Login server (a desktop build's server field), with the token kept for it.</summary>
        public void UseLoginServer(string loginAddress)
        {
            var url = LoginUrl(loginAddress);
            if (url == _loginAddress && _login != null)
            {
                return;
            }

            _loginAddress = url;
            _login = new LoginClient<CharacterView>(new UnityLoginTransport(url), new PlayerPrefsTokenStore(TokenKeyFor(url)));
        }

        /// <summary>The Login server's address in use.</summary>
        public string LoginAddress => _loginAddress;

        /// <summary>
        /// The Login server's URL for an address typed or given: a host and port (http://), or a full http(s):// URL.
        /// </summary>
        public static string LoginUrl(string loginAddress)
        {
            loginAddress = (loginAddress ?? "").Trim().TrimEnd('/');
            return loginAddress.Contains("://") ? loginAddress : "http://" + loginAddress;
        }

        /// <summary>
        /// A web build's Login server: the page's <c>?login=</c>, else <paramref name="pageLogin"/> (the one the page's
        /// deploy names), else <paramref name="fallback"/>.
        /// </summary>
        public static string LoginForPage(string pageUrl, string fallback, string pageLogin = null)
        {
            if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var page))
            {
                foreach (var pair in page.Query.TrimStart('?').Split('&'))
                {
                    if (pair.StartsWith("login=", StringComparison.Ordinal) && pair.Length > "login=".Length)
                    {
                        return Uri.UnescapeDataString(pair.Substring("login=".Length));
                    }
                }
            }

            return string.IsNullOrEmpty(pageLogin) ? fallback : pageLogin;
        }

        /// <summary>
        /// For a returning player (a kept token): loads the account and joins its character. Returns null once joining,
        /// <see cref="LoginErrors.SignedOut"/> when there's no usable token (show the join card), or another error code.
        /// </summary>
        public async Task<string> ResumeAsync()
        {
            var attempt = _joinAttempt;
            var error = await LoadAccountAsync();
            return error != null || attempt != _joinAttempt ? error : await PlayAsync();
        }

        /// <summary>
        /// Loads the signed-in account and its character without joining. Returns null, <see cref="LoginErrors.SignedOut"/>
        /// when there's no usable token, or another error code.
        /// </summary>
        public async Task<string> LoadAccountAsync()
        {
            if (!SignedIn)
            {
                return LoginErrors.SignedOut;
            }

            var account = await _login.GetAccountAsync();
            if (!account.Succeeded)
            {
                if (account.Error == LoginErrors.SignedOut)
                {
                    Account = null;
                    SignedOut?.Invoke();
                }

                return account.Error;
            }

            Account = account.Value;
            return null;
        }

        /// <summary>
        /// Stops a join on its way (the connecting pop-up's Cancel): a ticket still being fetched is dropped, and a
        /// connection not yet in the game closes without a failure.
        /// </summary>
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

        /// <summary>Makes a guest account with its first character, then joins. Returns an error code, or null.</summary>
        public async Task<string> CreateGuestAsync(string name, NewCharacter character)
        {
            var made = await _login.CreateGuestAsync(name, character);
            return made.Succeeded ? await ResumeAsync() : made.Error;
        }

        /// <summary>Signs in with a username and password, then joins. Returns an error code, or null.</summary>
        public async Task<string> SignInAsync(string username, string password)
        {
            var signedIn = await _login.SignInAsync(username, password);
            return signedIn.Succeeded ? await ResumeAsync() : signedIn.Error;
        }

        /// <summary>Signs in with a recovery code, then joins. Returns an error code, or null.</summary>
        public async Task<string> SignInWithCodeAsync(string code)
        {
            var signedIn = await _login.SignInWithCodeAsync(code);
            return signedIn.Succeeded ? await ResumeAsync() : signedIn.Error;
        }

        /// <summary>Gets a join ticket and joins the game server it names. Returns an error code, or null.</summary>
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
                return null; // cancelled while the ticket was on its way
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

            // A fresh connection for each ticket: the server takes one join per connection.
            Connect(url);
            _pending = new JoinRequest { Ticket = ticket.Value.Ticket };
            return null;
        }

        /// <summary>
        /// With accounts off (the editor): joins the editor's own game server as the character called
        /// <paramref name="name"/>, made with <paramref name="look"/> if it's new.
        /// </summary>
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

        /// <summary>Sets the password (a guest's first claims the account; changing it needs a recent sign-in). Returns an error code, or null.</summary>
        public async Task<string> SetPasswordAsync(string password)
        {
            var set = await _login.SetPasswordAsync(password);
            await RefreshAccountAsync();
            return set.Error;
        }

        /// <summary>Makes a new recovery code (voiding the old), shown once. Returns it, or an error code.</summary>
        public async Task<(string Code, string Error)> MakeRecoveryCodeAsync()
        {
            var made = await _login.MakeRecoveryCodeAsync();
            await RefreshAccountAsync();
            return (made.Value?.Code, made.Error);
        }

        /// <summary>Signs in again with the password, for a change that needs a recent sign-in. Returns an error code, or null.</summary>
        public async Task<string> ConfirmPasswordAsync(string password)
        {
            var signedIn = await _login.SignInAgainAsync(PlayerName, password);
            await RefreshAccountAsync();
            return signedIn.Error;
        }

        /// <summary>Signs out: leaves the game, revokes the session and forgets the token.</summary>
        public async Task SignOutAsync()
        {
            Leave();
            await _login.SignOutAsync();
            Account = null;
            SignedOut?.Invoke();
        }

        /// <summary>Reloads the account (after a change), keeping the old one if the Login server can't be reached.</summary>
        public async Task RefreshAccountAsync()
        {
            var account = await _login.GetAccountAsync();
            if (account.Succeeded)
            {
                Account = account.Value;
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
                // Browsers get the text written for them (keys ending _web), such as where a guest is kept.
                _content.Strings.SetVariant(Application.platform == RuntimePlatform.WebGLPlayer ? "web" : null);
            }

            Zone = ShapeLandWorld.StartOf(_content, _content.Shapes.All[0]).Zone;
            World = ShapeLandWorld.Create(_content, Zone);

            ContentLoaded?.Invoke(_content);
        }

        /// <summary>
        /// Once joined, asks to change the shape or colors where the player stands. The answer is a new
        /// <see cref="OwnSpawned"/>, or <see cref="LookRefused"/>.
        /// </summary>
        public void ChangeLook(LookChange change)
        {
            if (!Joined)
            {
                throw new InvalidOperationException("Change the look once joined.");
            }

            Session.Write(ShapeLandMessageIds.LookChange, change);
        }

        /// <summary>
        /// Renames the account at the Login server, then tells the game server, which shows everyone the new name.
        /// Returns an error code (<see cref="LoginErrors.NameTaken"/>, <see cref="LoginErrors.InvalidName"/>…), or null.
        /// </summary>
        public async Task<string> RenameAsync(string name)
        {
            var renamed = await _login.RenameAsync(name);
            if (!renamed.Succeeded)
            {
                return renamed.Error;
            }

            await RefreshAccountAsync();
            if (Joined && Session != null && !Session.Closed)
            {
                Session.Write(ShapeLandMessageIds.NameChanged, new NameChanged());
            }

            return null;
        }

        /// <summary>Says <paramref name="text"/> in chat, once joined; the line arrives back through <see cref="ChatReceived"/>.</summary>
        /// <summary>Leaves the game (the Esc menu's Leave game): closes the connection and goes back to the join screen.</summary>
        public void Leave()
        {
            if (Joined)
            {
                Log("left the game");
                EndSession(CloseReason.None);
            }
        }

        // Out of the world, by the connection closing or by leaving; the next Join connects afresh.
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

        /// <summary>Asks the server to send the player back to their spawn point.</summary>
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
                ShapeLandWorld.GroundHeight(_content, World)
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

#if UNITY_WEBGL && !UNITY_EDITOR
        // Plugins/WebGL/ShapeLandPage.jslib.
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern string ShapeLand_PageLogin();
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
                Log($"connection lost: {session.CloseReason} ({session.Error})");
                EndSession(session.CloseReason);
                return;
            }

            if (session.Closed)
            {
                // Idle after a refused join, the server drops the connection after its join deadline; that's no
                // failure, and the next join reconnects.
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
                            _devName = spawn.Name; // as the character was first named, whatever case was typed
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
                        // Slow to arrive, or the server restarted since: one more try with a fresh ticket.
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
                    // The connection closes next; the client doesn't rejoin by itself, so two tabs can't keep
                    // taking the character from each other.
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

                    // Before the player's arrival there: the world is built here, in the same frame, so the
                    // correction that follows lands in it and the player's movement never runs on the old zone.
                    EnterZone(zone, rebuild: true);
                    Session.Entities.GroundHeight = ShapeLandWorld.GroundHeight(_content, World);
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

        /// <summary>Before joining, shows <paramref name="zone"/> (the picked shape's start zone) behind the join screen.</summary>
        public void ShowZone(Zone zone)
        {
            if (_content != null && !Joined)
            {
                EnterZone(zone, rebuild: false);
            }
        }

        // Switches to a zone. The server's say rebuilds the world even for the zone shown, so nothing stays flipped
        // from before; just showing one keeps it if it's already shown.
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
