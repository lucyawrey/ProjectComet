using Comet.Protocol;
using MessagePack;
using ShapeLand.Shared.Content;

namespace ShapeLand.Shared.Messages
{
    public static class ShapeLandProtocol
    {
        public static readonly MessagePackSerializerOptions Options = ProtocolSerializer.CreateOptions(ShapeLandResolver.Instance);
    }
}
