// See https://aka.ms/new-console-template for more information

using GameGatewayServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<GatewayServerImpl>();
app.MapGet("/", () => "Gateway Server");

app.Run();
