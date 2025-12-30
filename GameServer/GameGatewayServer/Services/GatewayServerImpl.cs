using System.Collections.Concurrent;
using GameProtocol;
using GameProtocol.Grains;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace GameGatewayServer.Services;

public class GatewayServerImpl : GatewayServer.GatewayServerBase
{
    private readonly IClusterClient _clusterClient;
    private readonly IConnection _natsConnection;
    private readonly ILogger<GatewayServerImpl> _logger;

    public GatewayServerImpl(IClusterClient clusterClient, IConnection natsConnection, ILogger<GatewayServerImpl> logger)
    {
        _clusterClient = clusterClient;
        _natsConnection = natsConnection;
        _logger = logger;
    }

    public override Task<LoginResponse> Login(LoginRequest request, ServerCallContext context)
    {
        this._logger.LogInformation($"Player {request.PlayerId} is logged in");
        return Task.FromResult(new LoginResponse());
    }

    public override Task<LogoutResponse> Logout(LogoutRequest request, ServerCallContext context)
    {
        this._logger.LogInformation($"Player {request.PlayerId} is logged out");
        return Task.FromResult(new LogoutResponse());
    }

    public override async Task Connect(IAsyncStreamReader<ClientConnectionRequest> requestStream, IServerStreamWriter<ServerConnectionResponse> responseStream, ServerCallContext context)
    {
        string playerId = string.Empty;
        string? currentFieldId = null;
        var natsSubscriptions = new List<IAsyncSubscription>();
        var subscriptionLock = new SemaphoreSlim(1, 1);
        const int aoiLevel = 1;

        try
        {
            // NATS 메시지 수신 시, 클라이언트 스트림으로 전송하는 핸들러
            var natsHandler = new EventHandler<MsgHandlerEventArgs>(async (sender, args) =>
            {
                try
                {
                    var response = SubscribeResponse.Parser.ParseFrom(args.Message.Data);
                    if (response.PlayerPositionList.Any(p => p.PlayerId != playerId))
                    {
                        var message = new ServerConnectionResponse { WorldUpdate = response };
                        await responseStream.WriteAsync(message);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing and forwarding NATS message for player {PlayerId}", playerId);
                }
            });

            // NATS 구독을 갱신하는 함수
            async Task UpdateNatsSubscriptions(string newFieldId)
            {
                await subscriptionLock.WaitAsync(context.CancellationToken);
                try
                {
                    _logger.LogInformation("Player {PlayerId} is updating subscriptions from {OldFieldId} to {NewFieldId}", playerId, currentFieldId ?? "None", newFieldId);
                    
                    // 기존 구독 해지
                    foreach (var sub in natsSubscriptions)
                    {
                        sub.Unsubscribe();
                        sub.Dispose();
                    }
                    natsSubscriptions.Clear();

                    // 새 구독 시작
                    var topicIds = QuadTreeHelper.GetNeighborIds(newFieldId, aoiLevel);
                    _logger.LogInformation("Player {PlayerId} subscribing to {TopicCount} topics for new field {NewFieldId}.", playerId, topicIds.Count, newFieldId);
                    foreach (var topicId in topicIds)
                    {
                        var topic = $"world.{topicId}.updates";
                        var sub = _natsConnection.SubscribeAsync(topic, natsHandler);
                        natsSubscriptions.Add(sub);
                    }
                    
                    currentFieldId = newFieldId;
                }
                finally
                {
                    subscriptionLock.Release();
                }
            }
            
            // 클라이언트로부터 오는 메시지를 처리하는 읽기 루프
            await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
            {
                if (request.MessageCase != ClientConnectionRequest.MessageOneofCase.PositionUpdate) continue;

                var position = request.PositionUpdate;
                playerId = position.PlayerId;

                if (string.IsNullOrEmpty(playerId)) continue;
                
                var playerGrain = _clusterClient.GetGrain<IPlayerGrain>(playerId);
                await playerGrain.UpdatePosition(position);

                var newFieldId = QuadTreeHelper.GetNodeIdForPosition(position);

                if (currentFieldId != newFieldId)
                {
                    // 이전 구역에서 나감
                    if (!string.IsNullOrEmpty(currentFieldId))
                    {
                        var oldWorldGrain = _clusterClient.GetGrain<IWorldGrain>(currentFieldId);
                        await oldWorldGrain.Leave(playerId);
                    }

                    // 새 구역으로 진입
                    var newWorldGrain = _clusterClient.GetGrain<IWorldGrain>(newFieldId);
                    await newWorldGrain.Enter(position);
                    
                    _logger.LogInformation("Player {PlayerId} moved from {OldFieldId} to {NewFieldId}", playerId, currentFieldId ?? "None", newFieldId);

                    // NATS 구독 갱신
                    await UpdateNatsSubscriptions(newFieldId);
                }
                else
                {
                    // 같은 구역 내에서 위치만 업데이트
                    var worldGrain = _clusterClient.GetGrain<IWorldGrain>(newFieldId);
                    await worldGrain.UpdatePlayerPosition(position);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Player {PlayerId} communication stream cancelled.", playerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred in the communication stream for player {PlayerId}.", playerId);
        }
        finally
        {
            _logger.LogInformation("Player {PlayerId} communication stream ending. Cleaning up resources.", playerId);
            
            // NATS 구독 해지
            await subscriptionLock.WaitAsync();
            try
            {
                foreach (var sub in natsSubscriptions)
                {
                    sub.Unsubscribe();
                    sub.Dispose();
                }
                natsSubscriptions.Clear();
                _logger.LogInformation("Player {PlayerId} unsubscribed from all topics.", playerId);
            }
            finally
            {
                subscriptionLock.Release();
            }
            
            if (!string.IsNullOrEmpty(playerId) && !string.IsNullOrEmpty(currentFieldId))
            {
                var worldGrain = _clusterClient.GetGrain<IWorldGrain>(currentFieldId);
                await worldGrain.Leave(playerId);
                _logger.LogInformation("Player {PlayerId} left final grain {FinalFieldId}.", playerId, currentFieldId);
            }
        }
    }


}

    