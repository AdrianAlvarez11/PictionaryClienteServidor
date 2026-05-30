using PictionarySignalR.Hubs;
using PictionarySignalR.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<JuegoService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<JuegoHub>("/juegoHub");

app.Run();
