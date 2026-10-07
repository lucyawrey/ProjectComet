using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Comet.Protocol;

namespace Comet.Client
{
    /// <summary>
    /// The desktop and bot transport, on .NET's <see cref="ClientWebSocket"/>: connects, receives and sends
    /// on background tasks and queues frames for the polling thread. Not for web builds, which have no
    /// threads; the Unity package has a browser transport for those.
    /// </summary>
    public sealed class WebSocketTransport : IClientTransport
    {
        private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

        private readonly ClientWebSocket _socket = new ClientWebSocket();
        private readonly ConcurrentQueue<byte[]> _received = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<byte[]> _outgoing = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim _sendSignal = new SemaphoreSlim(0);
        private readonly int _maxFrameSize;
        private volatile TransportState _state = TransportState.Connecting;
        private volatile string? _error;
        private volatile bool _closing;

        /// <summary>Starts connecting to <paramref name="url"/>.</summary>
        /// <param name="maxFrameSize">Larger frames from the server close the connection.</param>
        public WebSocketTransport(Uri url, int maxFrameSize = 64 * 1024)
        {
            _maxFrameSize = maxFrameSize;
            Completion = RunAsync(url);
        }

        public TransportState State => _state;

        public string? Error => _error;

        /// <summary>Finishes when the connection is closed and the socket released.</summary>
        public Task Completion { get; }

        public void Send(ReadOnlySpan<byte> frame)
        {
            if (_state != TransportState.Open || _closing)
            {
                return;
            }

            _outgoing.Enqueue(frame.ToArray());
            _sendSignal.Release();
        }

        public bool TryReceive(out byte[] frame) => _received.TryDequeue(out frame!);

        public void Close()
        {
            _closing = true;
            _sendSignal.Release();
        }

        public void Dispose()
        {
            Close();
            _socket.Abort();
        }

        private async Task RunAsync(Uri url)
        {
            try
            {
                await _socket.ConnectAsync(url, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e) when (e is WebSocketException || e is System.Net.Http.HttpRequestException || e is ObjectDisposedException)
            {
                Finish(e.Message);
                return;
            }

            _state = TransportState.Open;
            var receiving = ReceiveLoopAsync();
            var sending = SendLoopAsync();

            // A normal close finishes when the server answers; give up waiting after a while.
            if (await Task.WhenAny(receiving, sending).ConfigureAwait(false) == sending)
            {
                await Task.WhenAny(receiving, Task.Delay(CloseTimeout)).ConfigureAwait(false);
            }

            _socket.Abort();
            await Task.WhenAll(receiving, sending).ConfigureAwait(false);
            _socket.Dispose();
            Finish(_error);
        }

        private void Finish(string? error)
        {
            _error = error;
            _state = TransportState.Closed;
        }

        private async Task SendLoopAsync()
        {
            try
            {
                while (true)
                {
                    await _sendSignal.WaitAsync().ConfigureAwait(false);
                    while (_outgoing.TryDequeue(out var frame))
                    {
                        await _socket.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, CancellationToken.None).ConfigureAwait(false);
                    }

                    if (_closing || _socket.State != WebSocketState.Open)
                    {
                        break;
                    }
                }

                if (_socket.State == WebSocketState.Open)
                {
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is WebSocketException || e is ObjectDisposedException)
            {
                _error ??= e.Message;
            }
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[_maxFrameSize];
            var length = 0;
            try
            {
                while (true)
                {
                    var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), CancellationToken.None).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (!_closing)
                        {
                            _error ??= "The server closed the connection.";
                        }

                        return;
                    }

                    length += result.Count;
                    if (result.EndOfMessage)
                    {
                        _received.Enqueue(buffer.AsSpan(0, length).ToArray());
                        length = 0;
                    }
                    else if (length == buffer.Length)
                    {
                        throw new ProtocolException("Server frame too large.");
                    }
                }
            }
            catch (Exception e) when (e is WebSocketException || e is ProtocolException || e is ObjectDisposedException)
            {
                if (!_closing)
                {
                    _error ??= e.Message;
                }
            }
            finally
            {
                // Wake the send loop so it notices the connection is gone.
                _closing = true;
                _sendSignal.Release();
            }
        }
    }
}
