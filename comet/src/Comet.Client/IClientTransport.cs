using System;

namespace Comet.Client
{
    public enum TransportState
    {
        Connecting,
        Open,

        /// <summary>Closed by either side, or failed to connect; <see cref="IClientTransport.Error"/> says which.</summary>
        Closed,
    }

    /// <summary>
    /// A connection to the game server that moves whole binary frames. Polled from one thread: a transport
    /// may receive in the background (desktop) or as the browser delivers messages (web), but frames only
    /// reach the session through <see cref="TryReceive"/>.
    /// </summary>
    public interface IClientTransport : IDisposable
    {
        TransportState State { get; }

        /// <summary>Why the connection failed or was lost; null while open or after a normal close.</summary>
        string? Error { get; }

        /// <summary>Queues a frame to send; the bytes are copied. Ignored unless open.</summary>
        void Send(ReadOnlySpan<byte> frame);

        /// <summary>Takes the next received frame, if any.</summary>
        bool TryReceive(out byte[] frame);

        /// <summary>Starts a normal close; <see cref="State"/> becomes <see cref="TransportState.Closed"/> when done.</summary>
        void Close();
    }
}
