using MessagePack;
using MessagePack.Resolvers;

namespace Comet.Protocol
{
    /// <summary>
    /// MessagePack setup for protocol messages. Formatters are source-generated, so no run-time code generation (IL2CPP, web builds).
    /// A game with its own messages passes the options from <see cref="CreateOptions"/> to the writer and reader.
    /// </summary>
    public static class ProtocolSerializer
    {
        public static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard.WithResolver(
            CompositeResolver.Create(ProtocolResolver.Instance, StandardResolver.Instance)
        );

        /// <summary>Comet's messages plus a game's, whose formatters come from <paramref name="gameResolver"/>.</summary>
        public static MessagePackSerializerOptions CreateOptions(IFormatterResolver gameResolver) =>
            MessagePackSerializerOptions.Standard.WithResolver(
                CompositeResolver.Create(gameResolver, ProtocolResolver.Instance, StandardResolver.Instance)
            );
    }

    [GeneratedMessagePackResolver]
    internal sealed partial class ProtocolResolver { }
}
