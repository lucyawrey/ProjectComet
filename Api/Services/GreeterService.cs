using Grpc.Core;
using IdGen;

namespace AzaleaGames.ProjectComet.Api.Services;

public class GreeterService : Greeter.GreeterBase
{
    private readonly ILogger<GreeterService> _logger;
    private readonly IIdGenerator<long> _idGen;
    public GreeterService(ILogger<GreeterService> logger, IIdGenerator<long> idGen)
    {
        _logger = logger;
        _idGen = idGen;
    }

    public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
    {
        return Task.FromResult(new HelloReply
        {
            Message = $"Hello {request.Name}. Your ID is: {_idGen.CreateId()}"
        });
    }
}
