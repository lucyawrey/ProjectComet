using MessagePack;
using MessagePack.Resolvers;

namespace Comet.Content
{
    /// <summary>
    /// MessagePack setup for compiled content. Formatters are source-generated, so there is no run-time code
    /// generation (needed for IL2CPP and web builds). Each game passes its own generated resolver for its content types.
    /// </summary>
    public static class ContentSerializer
    {
        /// <summary>Options that read and write Comet's content types and the game's, through <paramref name="gameResolver"/>.</summary>
        public static MessagePackSerializerOptions CreateOptions(IFormatterResolver gameResolver) => MessagePackSerializerOptions.Standard
            .WithResolver(CompositeResolver.Create(gameResolver, ContentResolver.Instance, StandardResolver.Instance));
    }

    [GeneratedMessagePackResolver]
    internal partial class ContentResolver
    {
    }
}
