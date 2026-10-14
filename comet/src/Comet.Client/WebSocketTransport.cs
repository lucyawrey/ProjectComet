using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Comet.Protocol;
using Comet.Protocol.Framing;

namespace Comet.Client
{
    /// <summary>
    /// The desktop and bot transport, on .NET's <see cref="ClientWebSocket"/>: connects, receives and sends
    /// on background tasks and queues frames for the polling thread. Not for web builds, which have no
    /// threads; the Unity package has a browser transport for those.
    /// </summary>
    public sealed class WebSocketTransport : IClientTransport
    {
        private readonly ClientWebSocket _socket = new ClientWebSocket();
        private readonly ConcurrentQueue<byte[]> _received = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<byte[]> _outgoing = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim _sendSignal = new SemaphoreSlim(0);
        private readonly int _maxFrameSize;
        private readonly TimeSpan _closeTimeout;
        private readonly TimeSpan _connectTimeout;
        private long _queuedBytes;
        private volatile TransportState _state = TransportState.Connecting;
        private readonly object _endLock = new object();
        private CloseReason _closeReason;
        private string? _error;
        private volatile bool _closing;

        /// <summary>Starts connecting to <paramref name="url"/>.</summary>
        /// <param name="maxFrameSize">Larger frames from the server close the connection.</param>
        /// <param name="closeTimeout">How long a close waits for the server's answer before giving up (default 5 s).</param>
        /// <param name="connectTimeout">How long connecting may take before it fails (default <see cref="TransportLimits.ConnectTimeoutSeconds"/>).</param>
        public WebSocketTransport(Uri url, int maxFrameSize = FrameReader.MaxServerFrameSize, TimeSpan? closeTimeout = null, TimeSpan? connectTimeout = null)
        {
            _maxFrameSize = maxFrameSize;
            _closeTimeout = closeTimeout ?? TimeSpan.FromSeconds(5);
            _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(TransportLimits.ConnectTimeoutSeconds);
            Completion = RunAsync(url);
        }

        public TransportState State => _state;

        public CloseReason CloseReason
        {
            get
            {
                lock (_endLock)
                {
                    return _closeReason;
                }
            }
        }

        public string? Error
        {
            get
            {
                lock (_endLock)
                {
                    return _error;
                }
            }
        }

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

        public bool TryReceive(out byte[] frame)
        {
            if (!_received.TryDequeue(out frame!))
            {
                return false;
            }

            Interlocked.Add(ref _queuedBytes, -frame.Length);
            return true;
        }

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
            using (var timeout = new CancellationTokenSource(_connectTimeout))
            {
                try
                {
                    await _socket.ConnectAsync(url, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    // Any failure to connect (refused, a bad address such as an http:// URL, an address that never
                    // answers, disposed meanwhile) ends the connection with its reason, never leaving it Connecting.
                    _socket.Dispose();
                    if (timeout.IsCancellationRequested)
                    {
                        End(CloseReason.ConnectTimedOut, "Timed out connecting to the server.");
                    }
                    else
                    {
                        End(CloseReason.ConnectFailed, e.Message);
                    }

                    _state = TransportState.Closed;
                    return;
                }
            }

            _state = TransportState.Open;
            try
            {
                var receiving = ReceiveLoopAsync();
                var sending = SendLoopAsync();

                // A normal close finishes when the server answers; give up waiting after a while.
                if (await Task.WhenAny(receiving, sending).ConfigureAwait(false) == sending)
                {
                    await Task.WhenAny(receiving, Task.Delay(_closeTimeout)).ConfigureAwait(false);
                }

                _socket.Abort();
                await Task.WhenAll(receiving, sending).ConfigureAwait(false);
            }
            finally
            {
                _socket.Dispose();
                _state = TransportState.Closed;
            }
        }

        // Records why the connection ended; the first reason wins, and none is kept for a close of our own.
        private void End(CloseReason reason, string error)
        {
            lock (_endLock)
            {
                if (_closeReason == CloseReason.None && !_closing)
                {
                    _closeReason = reason;
                    _error = error;
                }
            }
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
            catch (Exception e) when (e is WebSocketException || e is ObjectDisposedException || e is OperationCanceledException)
            {
                // Aborting the socket (Dispose, or a close the server didn't answer) cancels a pending send.
                End(CloseReason.Failed, e.Message);
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
                        End(result.CloseStatus == WebSocketCloseStatus.EndpointUnavailable ? CloseReason.ServerShutDown : CloseReason.ServerClosed,
                            string.IsNullOrEmpty(result.CloseStatusDescription) ? "The server closed the connection." : result.CloseStatusDescription!);

                        return;
                    }

                    length += result.Count;
                    if (result.EndOfMessage)
                    {
                        // Frames pile up while nothing takes them (a paused editor); past the cap, give up rather than
                        // keep everything.
                        if (Interlocked.Add(ref _queuedBytes, length) > TransportLimits.MaxQueuedBytes)
                        {
                            // Drop what's waiting too, so it isn't all handled at once on the game's return.
                            while (_received.TryDequeue(out _))
                            {
                            }

                            Interlocked.Exchange(ref _queuedBytes, 0);
                            End(CloseReason.FellBehind, "Fell too far behind the server (the game wasn't running for too long).");
                            return;
                        }

                        _received.Enqueue(buffer.AsSpan(0, length).ToArray());
                        length = 0;
                    }
                    else if (length == buffer.Length)
                    {
                        throw new ProtocolException("Server frame too large.");
                    }
                }
            }
            catch (ProtocolException e)
            {
                End(CloseReason.ProtocolError, e.Message);
            }
            catch (Exception e) when (e is WebSocketException || e is ObjectDisposedException || e is OperationCanceledException)
            {
                End(CloseReason.Failed, e.Message);
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
