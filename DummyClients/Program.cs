using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameProtocol;
using Grpc.Core;
using Grpc.Net.Client;

// 메인 메서드: 클라이언트 실행기
var numClients = args.Length > 0 ? int.Parse(args[0]) : 1;
Console.WriteLine($"Starting {numClients} dummy client(s)...");

var tasks = new List<Task>();
var cts = new CancellationTokenSource();

Console.CancelKeyPress += (sender, eventArgs) =>
{
    Console.WriteLine("Cancellation requested...");
    cts.Cancel();
    eventArgs.Cancel = true;
};

for (var i = 0; i < numClients; i++)
{
    tasks.Add(RunClientAsync($"dummy-client-{i + 1}", cts.Token));
}

await Task.WhenAll(tasks);


// 개별 클라이언트 로직
static async Task RunClientAsync(string playerId, CancellationToken cancellationToken)
{
    // 맵 경계 및 초기 위치/방향 설정
    const double MinX = -213.0;
    const double MaxX = 336.0;
    const double MinZ = -235.0;
    const double MaxZ = 177.0;
    const double YPosition = -4.6164; // 바닥 높이

    var random = new Random();
    var currentPosition = new PlayerPosition
    {
        PlayerId = playerId,
        X = random.NextDouble() * (MaxX - MinX) + MinX,
        Y = YPosition,
        Z = random.NextDouble() * (MaxZ - MinZ) + MinZ,
        Pitch = 0,
        Yaw = 0,
        Roll = 0
    };
    
    var direction = new Vector3((random.NextDouble() - 0.5) * 2, 0, (random.NextDouble() - 0.5) * 2).Normalized();
    const double speed = 5.0; // 초당 이동 거리

    // gRPC 채널 및 클라이언트 생성
    using var channel = GrpcChannel.ForAddress("http://localhost:5000"); // GatewayServer 주소
    var client = new GatewayServer.GatewayServerClient(channel);
    
    Console.WriteLine($"[{playerId}] Connecting and logging in...");
    try
    {
        await client.LoginAsync(new LoginRequest { PlayerId = playerId });
        
        // Subscribe 스트림을 백그라운드에서 실행
        _ = Task.Run(async () =>
        {
            Console.WriteLine($"[{playerId}] Starting to subscribe...");
            using var call = client.Subscribe(new SubscribeRequest { PlayerId = playerId }, cancellationToken: cancellationToken);
            try
            {
                await foreach (var update in call.ResponseStream.ReadAllAsync(cancellationToken))
                {
                    Console.WriteLine($"[{playerId}] Received update with {update.PlayerPositionList.Count} players.");
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                Console.WriteLine($"[{playerId}] Subscription stream cancelled.");
            }
        }, cancellationToken);

        // Publish 스트림 시작
        using var publishCall = client.Publish(cancellationToken: cancellationToken);
        Console.WriteLine($"[{playerId}] Starting to publish position...");
        
        var lastPublishTime = DateTime.UtcNow;
        
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var deltaTime = (now - lastPublishTime).TotalSeconds;
            lastPublishTime = now;
            
            // 위치 업데이트
            currentPosition.X += direction.X * speed * deltaTime;
            currentPosition.Z += direction.Z * speed * deltaTime;
            
            // 맵 경계를 벗어나면 방향 전환
            if (currentPosition.X > MaxX || currentPosition.X < MinX || currentPosition.Z > MaxZ || currentPosition.Z < MinZ)
            {
                direction = new Vector3(-direction.X, 0, -direction.Z);
            }

            try
            {
                await publishCall.RequestStream.WriteAsync(new PublishRequest { PlayerPosition = currentPosition });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{playerId}] Failed to publish: {ex.Message}");
                break;
            }

            await Task.Delay(200, cancellationToken); // 200ms 마다 위치 전송
        }
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
    {
        Console.WriteLine($"[{playerId}] Main task cancelled.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{playerId}] An error occurred: {ex.Message}");
    }
    finally
    {
        Console.WriteLine($"[{playerId}] Logging out...");
        try
        {
            await client.LogoutAsync(new LogoutRequest { PlayerId = playerId });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{playerId}] Logout failed: {ex.Message}");
        }
    }
}

// 간단한 3D 벡터 구조체
public struct Vector3
{
    public double X, Y, Z;

    public Vector3(double x, double y, double z)
    {
        X = x; Y = y; Z = z;
    }

    public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);

    public Vector3 Normalized()
    {
        var len = Length();
        return len > 0 ? new Vector3(X / len, Y / len, Z / len) : this;
    }
    
    public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3 operator -(Vector3 a) => new Vector3(-a.X, -a.Y, -a.Z);
    public static Vector3 operator *(Vector3 a, double d) => new Vector3(a.X * d, a.Y * d, a.Z * d);
}
