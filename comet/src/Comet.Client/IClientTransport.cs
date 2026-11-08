using System;

namespace Comet.Client
{
    public enum TransportState
    {
        Connecting,
        Open,

        /// <summary>Closed by either side, or failed to connect; <see cref="IClientTransport.CloseReason"/> says which.</summary>
        Closed,
    }

    /// <summary>Why a connection ended, as a code for the game to put in its own words.</summary>
    public enum CloseReason
    {
        /// <summary>Still open, or closed by this side.</summary>
        None,

        /// <summary>Couldn't connect: refused, a bad address, or a network error.</summary>
        ConnectFailed,

        /// <summary>Connecting took longer than <see cref="TransportLimits.ConnectTimeoutSeconds"/>.</summary>
        ConnectTimedOut,

        ServerClosed,

        /// <summary>The server closed the connection because it's shutting down.</summary>
        ServerShutDown,

        Failed,

        /// <summary>Received frames piled up past <see cref="TransportLimits.MaxQueuedBytes"/> while the game wasn't running.</summary>
        FellBehind,

        ProtocolError,

        /// <summary>The server stopped answering pings for <see cref="ClientSession.LostSeconds"/>.</summary>
        Lost,
    }

    /// <summary>
    /// A connection to the game server that moves whole binary frames. Polled from one thread: frames reach the session
    /// only through <see cref="TryReceive"/>, whether the transport receives in the background (desktop) or as the browser delivers them (web).
    /// </summary>
    public interface IClientTransport : IDisposable
    {
        TransportState State { get; }

        /// <summary>Why the connection failed or was lost; <see cref="CloseReason.None"/> while open or after closing it.</summary>
        CloseReason CloseReason { get; }

        /// <summary>The details of <see cref="CloseReason"/>, in English, for logs; null when there's no reason.</summary>
        string? Error { get; }

        /// <summary>Queues a frame to send; the bytes are copied. Ignored unless open.</summary>
        void Send(ReadOnlySpan<byte> frame);

        bool TryReceive(out byte[] frame);

        /// <summary>Starts a normal close; <see cref="State"/> becomes <see cref="TransportState.Closed"/> when done.</summary>
        void Close();
    }
}
