using System.Numerics;
using System.Threading.Tasks.Sources;
using GameProtocol;
using Grpc.Core;
using Grpc.Net.Client;

var numClients = args.Length > 0 ? int.Parse(args[0]) : 1;
Console.WriteLine($"{numClients}개의 더미 클라이언트를 생성하는 중");

var tasks = new List<Task>();
var cts = new CancellationTokenSource();

Console.CancelKeyPress += (sender, eventArgs) =>
{
    Console.WriteLine("더미 클라이언트 종료하는 중");
    cts.Cancel();
    eventArgs.Cancel = true;
};

for (var i = 0; i < numClients; i++)
{
    tasks.Add(RunClientAsync($"dummy-client-{i}", cts.Token));
}
await Task.WhenAll(tasks);

static async Task RunClientAsync(string playerId, CancellationToken cancellationToken)
{
    double MinX = GameProtocol.Constants.Constants.MinAreaX;
    double MaxX = GameProtocol.Constants.Constants.MaxAreaX;
    double MinZ = GameProtocol.Constants.Constants.MinAreaZ;
    double MaxZ = GameProtocol.Constants.Constants.MaxAreaZ;
    double speed = 5.0;

    const double yPosition = -4.6164; // 높이
    var random = new Random();
    var currentPosition = new PlayerPosition
    {
        PlayerId = playerId,
        X = random.NextDouble() * (MaxX - MinX) + MinX,
        Y = yPosition,
        Z = random.NextDouble() * (MaxZ - MinZ) + MinZ,
        Pitch = 0,
        Yaw = 0,
        Roll = 0,
    };
    
    var direction = new Vector3((float)((random.NextDouble() - 0.5) * 2), 0, (float)((random.NextDouble() - 0.5) * 2));
    var normalized = direction = Vector3.Normalize(direction);

    using var channel = GrpcChannel.ForAddress("http://127.0.0.1:5001", new GrpcChannelOptions
    {
        Credentials = ChannelCredentials.Insecure
    });
    var client = new GatewayServer.GatewayServerClient(channel);

    Console.WriteLine($"Player {playerId} 로그인 수행");
    await client.LoginAsync(new LoginRequest { PlayerId = playerId });

    // 위치 정보 주기적으로 전송
    using var publishCall = client.Publish();
    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // 위치 업데이트
            currentPosition.X += normalized.X * speed * 0.1;
            currentPosition.Z += normalized.Z * speed * 0.1;

            // 경계에 닿으면 반대쪽으로 뛰어가기
            if (currentPosition.X < MinX || currentPosition.X > MaxX)
            {
                normalized.X *= -1;
                currentPosition.X = Math.Clamp(currentPosition.X, MinX, MaxX);
            }
            if (currentPosition.Z < MinZ || currentPosition.Z > MaxZ)
            {
                normalized.Z *= -1;
                currentPosition.Z = Math.Clamp(currentPosition.Z, MinZ, MaxZ);
            }

            await publishCall.RequestStream.WriteAsync(new PublishRequest { PlayerPosition = currentPosition });
            await Task.Delay(100, cancellationToken);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Player {playerId} publish error: {ex.Message}");
    }
    finally
    {
        await publishCall.RequestStream.CompleteAsync();
        
        Console.WriteLine($"Player {playerId} publish stream finished.");
        try
        {
            await client.LogoutAsync(new LogoutRequest { PlayerId = playerId });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Player {playerId} logout error: {ex.Message}");
        }
    }
}