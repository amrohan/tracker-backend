using PersonalTracker.Api;
using PersonalTracker.Application;
using PersonalTracker.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApi(builder.Configuration);

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

app.UseApi();
app.MapApi();

app.Run();
