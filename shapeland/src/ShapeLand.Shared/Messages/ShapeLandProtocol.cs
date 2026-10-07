using Comet.Protocol;
using MessagePack;
using ShapeLand.Shared.Content;

namespace ShapeLand.Shared.Messages
{
    /// <summary>Serializer options for ShapeLand's connections: Comet's messages plus ShapeLand's.</summary>
    public static class ShapeLandProtocol
    {
        public static readonly MessagePackSerializerOptions Options = ProtocolSerializer.CreateOptions(ShapeLandResolver.Instance);
    }
}
