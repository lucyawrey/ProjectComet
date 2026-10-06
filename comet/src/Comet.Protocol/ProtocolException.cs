using System;

namespace Comet.Protocol
{
    /// <summary>A peer sent data that breaks the protocol. Servers drop the connection.</summary>
    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message)
            : base(message)
        {
        }

        public ProtocolException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
