// See https://aka.ms/new-console-template for more information

using GameGatewayServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client;
using Orleans.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseOrleansClient(clientBuilder =>
{
    clientBuilder.UseLocalhostClustering();
});

// NATS Connection을 싱글톤으로 등록
builder.Services.AddSingleton<IConnection>(sp =>
{
    var factory = new ConnectionFactory();
    // NATS 서버 주소. 설정 파일에서 가져오는 것을 권장.
    var connection = factory.CreateConnection("nats://localhost:4222"); 
    return connection;
});

builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<GatewayServerImpl>();
app.MapGet("/", () => "Gateway Server");

app.Run();
