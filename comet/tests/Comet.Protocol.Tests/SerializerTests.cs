using Comet.Protocol.Messages;
using MessagePack;

namespace Comet.Protocol.Tests;

public class SerializerTests
{
    // IL2CPP and web builds can't generate code at run time, so every message needs a source-generated formatter.
    [Fact]
    public void EveryMessageHasASourceGeneratedFormatter()
    {
        var messageTypes = typeof(MessageIds).Assembly.GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(MessagePackObjectAttribute), false).Length > 0)
            .ToList();
        var getFormatter = typeof(IFormatterResolver).GetMethod(nameof(IFormatterResolver.GetFormatter))!;
        var resolver = (IFormatterResolver)typeof(ProtocolSerializer).Assembly
            .GetType("Comet.Protocol.ProtocolResolver")!
            .GetField("Instance")!
            .GetValue(null)!;

        Assert.NotEmpty(messageTypes);
        Assert.All(messageTypes, t => Assert.NotNull(getFormatter.MakeGenericMethod(t).Invoke(resolver, null)));
    }
}
