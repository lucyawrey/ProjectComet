using System;
using Comet.Client;
using MessagePack;
using UnityEngine;

namespace Comet.Unity
{
    /// <summary>
    /// Drives a client session from Unity's frame loop: it handles received frames before any other script's Update,
    /// and a <see cref="CometFlush"/> on the same object sends what the frame wrote after every other LateUpdate.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class CometConnection : MonoBehaviour
    {
        public ClientSession? Session { get; private set; }

        /// <summary>Seconds on a real-time clock, unaffected by pausing or time scale.</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        public ClientSession Connect(
            Uri url,
            MessagePackSerializerOptions options,
            float teleportSpeed = float.PositiveInfinity,
            float gravity = 0,
            Func<System.Numerics.Vector3, float?>? groundHeight = null
        )
        {
            Disconnect();
            if (GetComponent<CometFlush>() == null)
            {
                gameObject.AddComponent<CometFlush>().hideFlags = HideFlags.HideInInspector;
            }

            Session = new ClientSession(CreateTransport(url), options, teleportSpeed: teleportSpeed, gravity: gravity, groundHeight: groundHeight);
            return Session;
        }

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
