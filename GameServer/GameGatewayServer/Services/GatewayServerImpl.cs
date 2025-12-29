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

    public override async Task<PublishResponse> Publish(IAsyncStreamReader<PublishRequest> requestStream, ServerCallContext context)
    {
        string playerId = string.Empty;
        string currentFieldId = string.Empty;

        await foreach (var request in requestStream.ReadAllAsync())
        {
            var position = request.PlayerPosition;
            playerId = position.PlayerId;

            if (string.IsNullOrEmpty(playerId)) continue;

            var newFieldId = QuadTreeHelper.GetNodeIdForPosition(position);
            if (currentFieldId != newFieldId)
            {
                // 이전 구역에서 나감
                if (!string.IsNullOrEmpty(currentFieldId))
                {
                    var oldWorldGrain = _clusterClient.GetGrain<IWorldGrain>(currentFieldId);
                    _ = oldWorldGrain.Leave(playerId);
                }

                // 새 구역으로 진입
                var newWorldGrain = _clusterClient.GetGrain<IWorldGrain>(newFieldId);
                await newWorldGrain.Enter(position);
                
                _logger.LogInformation($"Player {playerId} moved from {currentFieldId} to {newFieldId}");
            }
            else
            {
                // 같은 구역 내에서 위치만 업데이트
                if (!string.IsNullOrEmpty(currentFieldId))
                {
                    var worldGrain = _clusterClient.GetGrain<IWorldGrain>(currentFieldId);
                    _ = worldGrain.UpdatePlayerPosition(position);
                }
            }
        }
        
        _logger.LogInformation($"Player {playerId} publish stream ended.");
        
        if (!string.IsNullOrEmpty(playerId) && !string.IsNullOrEmpty(currentFieldId))
        {
            var worldGrain = _clusterClient.GetGrain<IWorldGrain>(currentFieldId);
            await worldGrain.Leave(playerId);
            _logger.LogInformation($"Player {playerId} left grain {currentFieldId} on stream end.");
        }

        return new PublishResponse { Message = "OK" };
    }

    public override async Task Subscribe(SubscribeRequest request, IServerStreamWriter<SubscribeResponse> responseStream, ServerCallContext context)
    {
        var playerId = request.PlayerId;
        const int aoiLevel = 1;
        _logger.LogInformation($"Player {playerId} trying to subscribe with AOI Level {aoiLevel}.");
        
        var playerGrain =  _clusterClient.GetGrain<IPlayerGrain>(playerId);
        var currentFieldId = await playerGrain.GetFieldId();
        if (string.IsNullOrEmpty(currentFieldId))
        {
            _logger.LogWarning($"Cannot find FieldID for player {playerId}. Make sure to publish position first.");
            return;
        }
        
        var topicIds = QuadTreeHelper.GetNeighborIds(currentFieldId, aoiLevel);
        var subscriptions = new List<IAsyncSubscription>();
        _logger.LogInformation($"Player {playerId} is subscribing to {topicIds.Count} topics for field {currentFieldId}.");
        try
        {
            var handler = new EventHandler<MsgHandlerEventArgs>(async (sender, args) =>
            {
                try
                {
                    var response = SubscribeResponse.Parser.ParseFrom(args.Message.Data);
                    if (response.PlayerPositionList.Any())
                    {
                        await responseStream.WriteAsync(response);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing NATS message for player {PlayerId}", playerId);
                }
            });

            foreach (var topicId in topicIds)
            {
                var topic = $"world.{topicId}.updates";
                var sub = _natsConnection.SubscribeAsync(topic, handler);
                subscriptions.Add(sub);
                _logger.LogDebug($"Player {playerId} subscribed to {topic}.");
            }
            
            while (!context.CancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, context.CancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation($"Player {playerId} subscription cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"An error occurred during player {playerId} subscription.");
        }
        finally
        {
            _logger.LogInformation($"Player {playerId} unsubscribing from {subscriptions.Count} topics.");
            foreach (var sub in subscriptions)
            {
                sub.Unsubscribe();
                sub.Dispose();
            }
        }
    }
}

    