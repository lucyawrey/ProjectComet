using System;

namespace Comet.Client
{
    public enum TransportState
    {
        Connecting,
        Open,

        Closed,
    }

    public enum CloseReason
    {
        /// <summary>Still open, or closed by this side.</summary>
        None,

        ConnectFailed,

        ConnectTimedOut,

        ServerClosed,

        ServerShutDown,

        Failed,

        FellBehind,

        ProtocolError,

        Lost,
    }

    public interface IClientTransport : IDisposable
    {
        TransportState State { get; }

        CloseReason CloseReason { get; }

        /// <summary>The details of <see cref="CloseReason"/>, in English, for logs; null when there's no reason.</summary>
        string? Error { get; }

        /// <summary>Queues a frame to send; the bytes are copied. Ignored unless open.</summary>
        void Send(ReadOnlySpan<byte> frame);

        bool TryReceive(out byte[] frame);

        void Close();
    }
}
