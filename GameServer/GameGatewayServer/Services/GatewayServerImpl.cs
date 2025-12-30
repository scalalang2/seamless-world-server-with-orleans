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
        var natsSubscriptions = new Dictionary<string, IAsyncSubscription>();
        var subscriptionLock = new SemaphoreSlim(1, 1);
        const int aoiLevel = 1;

        // NATS 메시지 수신 시, 클라이언트 스트림으로 전송하는 핸들러
        var natsHandler = new EventHandler<MsgHandlerEventArgs>(async (sender, args) =>
        {
            try
            {
                var worldEvent = WorldEvent.Parser.ParseFrom(args.Message.Data);
                switch (worldEvent.EventCase)
                {
                    case WorldEvent.EventOneofCase.Positions:
                        if (worldEvent.Positions.PlayerPositionList.Any(p => p.PlayerId != playerId))
                        {
                            var positionMessage = new ServerConnectionResponse { WorldUpdate = worldEvent.Positions };
                            await responseStream.WriteAsync(positionMessage, context.CancellationToken);
                        }
                        break;
                    
                    case WorldEvent.EventOneofCase.PlayerLeft:
                        if (worldEvent.PlayerLeft.PlayerId != playerId)
                        {
                            var leftMessage = new ServerConnectionResponse { PlayerLeft = worldEvent.PlayerLeft };
                            await responseStream.WriteAsync(leftMessage, context.CancellationToken);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing and forwarding NATS message for player {PlayerId}", playerId);
            }
        });

        // NATS 구독을 갱신하는 함수
        async Task UpdateNatsSubscriptions(string newFieldId, string oldFieldId, CancellationToken cancellationToken)
        {
            await subscriptionLock.WaitAsync(cancellationToken);
            try
            {
                if (cancellationToken.IsCancellationRequested) return;

                if(!playerId.StartsWith("dummy-client")) _logger.LogInformation("Player {PlayerId} updating subscriptions from {OldFieldId} to {NewFieldId}", playerId, oldFieldId ?? "None", newFieldId);

                var oldTopicIds = string.IsNullOrEmpty(oldFieldId)
                    ? new HashSet<string>()
                    : QuadTreeHelper.GetNeighborIds(oldFieldId, aoiLevel).Select(id => $"world.{id}.updates").ToHashSet();
                
                var newTopicIds = QuadTreeHelper.GetNeighborIds(newFieldId, aoiLevel)
                    .Select(id => $"world.{id}.updates")
                    .ToHashSet();

                var topicsToUnsubscribe = oldTopicIds.Except(newTopicIds).ToList();
                var topicsToSubscribe = newTopicIds.Except(oldTopicIds).ToList();

                if (topicsToUnsubscribe.Any())
                {
                    if(!playerId.StartsWith("dummy-client")) _logger.LogInformation("Player {PlayerId} unsubscribing from {TopicCount} topics.", playerId, topicsToUnsubscribe.Count);
                    
                    var leavingPlayerLists = await Task.WhenAll(
                        topicsToUnsubscribe.Select(async topic =>
                        {
                            var fieldId = topic.Replace("world.", "").Replace(".updates", "");
                            var worldGrain = _clusterClient.GetGrain<IWorldGrain>(fieldId);
                            return await worldGrain.GetPlayers();
                        })
                    );

                    foreach (var topic in topicsToUnsubscribe)
                    {
                        if (natsSubscriptions.TryGetValue(topic, out var sub))
                        {
                            sub.Unsubscribe();
                            sub.Dispose();
                            natsSubscriptions.Remove(topic);
                        }
                    }

                    var allLeavingPlayers = leavingPlayerLists.SelectMany(list => list);
                    foreach (var p in allLeavingPlayers)
                    {
                        if (p.PlayerId != playerId)
                        {
                            var leftMessage = new ServerConnectionResponse { PlayerLeft = new PlayerLeft { PlayerId = p.PlayerId } };
                            await responseStream.WriteAsync(leftMessage, cancellationToken);
                        }
                    }
                }

                if (topicsToSubscribe.Any())
                {
                    if(!playerId.StartsWith("dummy-client")) _logger.LogInformation("Player {PlayerId} subscribing to {TopicCount} new topics.", playerId, topicsToSubscribe.Count);
                    
                    foreach (var topic in topicsToSubscribe)
                    {
                        var sub = _natsConnection.SubscribeAsync(topic, natsHandler);
                        natsSubscriptions[topic] = sub;
                    }
                }
            }
            finally
            {
                subscriptionLock.Release();
            }
        }
        
        try
        {
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
                position.FieldId = newFieldId;

                if (currentFieldId != newFieldId)
                {
                    var oldFieldId = currentFieldId;
                    currentFieldId = newFieldId; // 즉시 업데이트하여 레이스 컨디션 방지

                    // 이전 구역에 Leave 메시지 전송
                    if (!string.IsNullOrEmpty(oldFieldId))
                    {
                        var oldWorldGrain = _clusterClient.GetGrain<IWorldGrain>(oldFieldId);
                        await oldWorldGrain.Leave(playerId);
                    }

                    // 새 구역에 Enter 메시지 전송
                    var newWorldGrain = _clusterClient.GetGrain<IWorldGrain>(newFieldId);
                    await newWorldGrain.Enter(position);
                    
                    if(!playerId.StartsWith("dummy-client")) _logger.LogInformation("Player {PlayerId} moved from {OldFieldId} to {NewFieldId}", playerId, oldFieldId ?? "None", newFieldId);
                    
                    _ = Task.Run(() => UpdateNatsSubscriptions(newFieldId, oldFieldId, context.CancellationToken), context.CancellationToken);
                }
                else
                {
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
            
            await subscriptionLock.WaitAsync();
            try
            {
                _logger.LogInformation("Player {PlayerId} unsubscribing from all {TopicCount} topics.", playerId, natsSubscriptions.Count);
                foreach (var sub in natsSubscriptions.Values)
                {
                    sub.Unsubscribe();
                    sub.Dispose();
                }
                natsSubscriptions.Clear();
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