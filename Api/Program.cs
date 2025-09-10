using AzaleaGames.ProjectComet.Api.Entities;
using AzaleaGames.ProjectComet.Api.Services;
using IdGen;
using IdGen.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddIdGen(0, () => new IdGeneratorOptions(
    new IdStructure(41, 10, 12),
    new DefaultTimeSource(DateTime.UnixEpoch, TimeSpan.FromSeconds(1))
));
builder.Services.AddDbContext<ApiDbContext>();
builder.Services.AddGrpc();
builder.Services.AddGrpcReflection();

var app = builder.Build();

// TODO replace with migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
    db.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
app.MapGrpcService<GreeterService>();

IWebHostEnvironment env = app.Environment;
if (env.IsDevelopment())
{
    app.MapGrpcReflectionService();
}

app.Run();
