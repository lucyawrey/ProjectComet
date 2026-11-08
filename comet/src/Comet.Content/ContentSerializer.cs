using MessagePack;
using MessagePack.Resolvers;

namespace Comet.Content
{
    /// <summary>
    /// MessagePack setup for compiled content; formatters are source-generated (IL2CPP and web builds forbid run-time code generation).
    /// </summary>
    public static class ContentSerializer
    {
        public static MessagePackSerializerOptions CreateOptions(IFormatterResolver gameResolver) =>
            MessagePackSerializerOptions.Standard.WithResolver(
                CompositeResolver.Create(gameResolver, ContentResolver.Instance, StandardResolver.Instance)
            );
    }

    [GeneratedMessagePackResolver]
    internal sealed partial class ContentResolver { }
}
