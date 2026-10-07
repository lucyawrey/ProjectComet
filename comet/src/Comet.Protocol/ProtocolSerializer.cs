using MessagePack;
using MessagePack.Resolvers;

namespace Comet.Protocol
{
    /// <summary>
    /// MessagePack setup for protocol messages. Formatters are source-generated, so there is no
    /// run-time code generation (needed for IL2CPP and web builds). A game with its own messages
    /// creates options with its generated resolver and passes them to <see cref="Framing.MessageWriter"/>
    /// and <see cref="Framing.FrameReader.Decode{T}"/>.
    /// </summary>
    public static class ProtocolSerializer
    {
        /// <summary>Comet's messages only.</summary>
        public static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard
            .WithResolver(CompositeResolver.Create(ProtocolResolver.Instance, StandardResolver.Instance));

        /// <summary>Comet's messages plus a game's, whose formatters come from <paramref name="gameResolver"/>.</summary>
        public static MessagePackSerializerOptions CreateOptions(IFormatterResolver gameResolver) => MessagePackSerializerOptions.Standard
            .WithResolver(CompositeResolver.Create(gameResolver, ProtocolResolver.Instance, StandardResolver.Instance));
    }

    [GeneratedMessagePackResolver]
    internal partial class ProtocolResolver
    {
    }
}
