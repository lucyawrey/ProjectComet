#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using Comet.Client;
using Comet.Protocol.Framing;

namespace Comet.Unity
{
    /// <summary>
    /// The web transport, on the browser's WebSocket (Plugins/WebGL/CometWebSocket.jslib). The browser receives
    /// between frames and queues messages in JavaScript; <see cref="TryReceive"/> copies them out, so nothing
    /// calls into C# from JavaScript.
    /// </summary>
    public sealed class BrowserWebSocketTransport : IClientTransport
    {
        private readonly int _id;
        private readonly int _maxFrameSize;
        private byte[] _sendBuffer = new byte[1024];
        private string? _error;
        private bool _disposed;

        /// <summary>Starts connecting to <paramref name="url"/>.</summary>
        /// <param name="maxFrameSize">Larger frames from the server close the connection.</param>
        public BrowserWebSocketTransport(Uri url, int maxFrameSize = FrameReader.MaxServerFrameSize)
        {
            _maxFrameSize = maxFrameSize;
            _id = CometWs_Open(url.AbsoluteUri);
        }

        public TransportState State => _disposed ? TransportState.Closed : (TransportState)CometWs_State(_id);

        public string? Error
        {
            get
            {
                if (_error == null && !_disposed)
                {
                    _error = CometWs_Error(_id);
                }

                return _error;
            }
        }

        public void Send(ReadOnlySpan<byte> frame)
        {
            if (_disposed)
            {
                return;
            }

            // Imports take arrays (pinned while the call runs), so stage the frame in a reused buffer.
            if (_sendBuffer.Length < frame.Length)
            {
                _sendBuffer = new byte[Math.Max(frame.Length, _sendBuffer.Length * 2)];
            }

            frame.CopyTo(_sendBuffer);
            CometWs_Send(_id, _sendBuffer, frame.Length);
        }

        public bool TryReceive(out byte[] frame)
        {
            var length = _disposed ? -1 : CometWs_NextLength(_id);
            if (length < 0)
            {
                frame = Array.Empty<byte>();
                return false;
            }

            if (length > _maxFrameSize)
            {
                _error = "Server frame too large.";
                Close();
                frame = Array.Empty<byte>();
                return false;
            }

            frame = new byte[length];
            CometWs_Receive(_id, frame, length);

            return true;
        }

        public void Close()
        {
            if (!_disposed)
            {
                CometWs_Close(_id);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _error = Error;
            _disposed = true;
            CometWs_Free(_id);
        }

        [DllImport("__Internal")]
        private static extern int CometWs_Open(string url);

        [DllImport("__Internal")]
        private static extern int CometWs_State(int id);

        [DllImport("__Internal")]
        private static extern string? CometWs_Error(int id);

        [DllImport("__Internal")]
        private static extern void CometWs_Send(int id, byte[] data, int length);

        [DllImport("__Internal")]
        private static extern int CometWs_NextLength(int id);

        [DllImport("__Internal")]
        private static extern int CometWs_Receive(int id, byte[] buffer, int capacity);

        [DllImport("__Internal")]
        private static extern void CometWs_Close(int id);

        [DllImport("__Internal")]
        private static extern void CometWs_Free(int id);
    }
}
#endif
