using System;
using Comet.Client;
using MessagePack;
using UnityEngine;

namespace Comet.Unity
{
    /// <summary>
    /// Drives a client session from Unity's frame loop: it handles received frames before any other script's
    /// Update, and a <see cref="CometFlush"/> on the same object sends what the frame wrote after every other
    /// script's LateUpdate. Game scripts subscribe to <see cref="Session"/>'s events and write their messages
    /// to it during the frame.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class CometConnection : MonoBehaviour
    {
        /// <summary>The current session, or null before <see cref="Connect"/>.</summary>
        public ClientSession? Session { get; private set; }

        /// <summary>Seconds on the clock the session runs on: real time, unaffected by pausing or time scale.</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>Starts a new session to <paramref name="url"/>, closing any current one.</summary>
        /// <param name="options">Serializer options covering Comet's messages and the game's.</param>
        /// <param name="teleportSpeed">Other entities moving faster than this jump instead of gliding (see <see cref="RemoteEntities"/>).</param>
        /// <param name="gravity">For dead-reckoning other entities in the air.</param>
        /// <param name="groundHeight">The highest ground under a position at or below it, so dead reckoning never goes below it.</param>
        public ClientSession Connect(Uri url, MessagePackSerializerOptions options, float teleportSpeed = float.PositiveInfinity,
            float gravity = 0, Func<System.Numerics.Vector3, float?> groundHeight = null)
        {
            Disconnect();
            if (GetComponent<CometFlush>() == null)
            {
                gameObject.AddComponent<CometFlush>().hideFlags = HideFlags.HideInInspector;
            }

            Session = new ClientSession(CreateTransport(url), options, teleportSpeed: teleportSpeed, gravity: gravity, groundHeight: groundHeight);
            return Session;
        }

        /// <summary>Closes the current session, if any.</summary>
        public void Disconnect()
        {
            if (Session == null)
            {
                return;
            }

            var transport = Session.Transport;
            Session = null;
            transport.Close();
            // .NET's transport finishes the close handshake (or gives up after a few seconds) and releases its
            // socket by itself; disposing it now would abort the close. The browser's still closes cleanly when freed.
            if (!(transport is WebSocketTransport))
            {
                transport.Dispose();
            }
        }

        /// <summary>The platform's transport: the browser's WebSocket on the web, .NET's elsewhere.</summary>
        public static IClientTransport CreateTransport(Uri url)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new BrowserWebSocketTransport(url);
#else
            return new WebSocketTransport(url);
#endif
        }

        private void Update() => Session?.Update(Now);

        private void OnDestroy() => Disconnect();
    }
}
