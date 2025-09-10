using AzaleaGames.ProjectComet.Api.Entities;
using Grpc.Core;
using IdGen;

namespace AzaleaGames.ProjectComet.Api.Services;

public class GreeterService : Greeter.GreeterBase
{
    private readonly ILogger<GreeterService> _logger;
    private readonly ApiDbContext _db;
    private readonly IIdGenerator<long> _idGen;
    public GreeterService(ILogger<GreeterService> logger, ApiDbContext db, IIdGenerator<long> idGen)
    {
        _logger = logger;
        _db = db;
        _idGen = idGen;
    }

    public override async Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
    {
        _db.Users.Add(new User
        {
            Handle = _idGen.CreateId(),
            Username = request.Name,
            PasswordHash = "TODO: SETUP PASSWORD HASHING"
        });
        await _db.SaveChangesAsync();
        return new HelloReply
        {
            Message = $"Hello {request.Name}."
        };
    }
}
