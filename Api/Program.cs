using AzaleaGames.ProjectComet.Api.Services;
using IdGen;
using IdGen.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();
builder.Services.AddGrpcReflection();
builder.Services.AddIdGen(0, () => new IdGeneratorOptions(
    new IdStructure(41, 10, 12),
    new DefaultTimeSource(DateTime.UnixEpoch, TimeSpan.FromSeconds(1))
));

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<GreeterService>();

IWebHostEnvironment env = app.Environment;
if (env.IsDevelopment())
{
    app.MapGrpcReflectionService();
}

app.Run();
