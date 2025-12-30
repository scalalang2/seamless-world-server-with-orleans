// See https://aka.ms/new-console-template for more information

using System.Net;
using GameGatewayServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client;
using Orleans.Hosting;
using Orleans.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Any, 5001, listenOptions => { listenOptions.Protocols = HttpProtocols.Http2; });
});

builder.Host.UseOrleansClient(clientBuilder =>
{
    clientBuilder.UseLocalhostClustering()
        .Services.AddSerializer(builder => builder.AddProtobufSerializer());
});

var factory = new ConnectionFactory();
var connection = factory.CreateConnection("nats://localhost:4222");
builder.Services.AddSingleton<IConnection>(connection);
builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<GatewayServerImpl>();
app.MapGet("/", () => "Gateway Server");

app.Run();
