using AzaleaGames.ProjectComet.DataCenter.Entities;
using Grpc.Core;
using IdGen;

namespace AzaleaGames.ProjectComet.DataCenter.Services;

public class GreeterService : Greeter.GreeterBase
{
    private readonly ILogger<GreeterService> _logger;
    private readonly DataCenterDbContext _db;
    private readonly IIdGenerator<long> _idGen;
    public GreeterService(ILogger<GreeterService> logger, DataCenterDbContext db, IIdGenerator<long> idGen)
    {
        _logger = logger;
        _db = db;
        _idGen = idGen;
    }

    public override async Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
    {
        _db.User.Add(new User
        {
            Handle = _idGen.CreateId(),
            Username = request.Name,
            DisplayName = request.Name,
            PasswordHash = "TODO: SETUP PASSWORD HASHING"
        });
        await _db.SaveChangesAsync();
        return new HelloReply
        {
            Message = $"Hello {request.Name}."
        };
    }
}
