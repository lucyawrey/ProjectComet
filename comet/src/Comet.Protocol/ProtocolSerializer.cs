using MessagePack;
using MessagePack.Resolvers;

namespace Comet.Protocol
{
    public static class ProtocolSerializer
    {
        public static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard.WithResolver(
            CompositeResolver.Create(ProtocolResolver.Instance, StandardResolver.Instance)
        );

        public static MessagePackSerializerOptions CreateOptions(IFormatterResolver gameResolver) =>
            MessagePackSerializerOptions.Standard.WithResolver(
                CompositeResolver.Create(gameResolver, ProtocolResolver.Instance, StandardResolver.Instance)
            );
    }

    [GeneratedMessagePackResolver]
    internal sealed partial class ProtocolResolver { }
}
