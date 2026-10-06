using MessagePack;
using MessagePack.Resolvers;

namespace Comet.Protocol
{
    /// <summary>
    /// MessagePack setup for protocol messages. Formatters are source-generated, so there is no
    /// run-time code generation (needed for IL2CPP and web builds).
    /// </summary>
    public static class ProtocolSerializer
    {
        public static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard
            .WithResolver(CompositeResolver.Create(ProtocolResolver.Instance, StandardResolver.Instance));
    }

    [GeneratedMessagePackResolver]
    internal partial class ProtocolResolver
    {
    }
}
