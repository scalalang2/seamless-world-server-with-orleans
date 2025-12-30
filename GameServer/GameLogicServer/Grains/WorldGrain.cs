using GameProtocol;
using GameProtocol.Grains;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using NATS.Client;
using Orleans;
using Orleans.Runtime;

namespace GameLogicServer.Grains;

public class WorldGrain : Grain, IWorldGrain
{
    private readonly IConnection _natsConnection;
    private readonly ILogger<WorldGrain> _logger;
    
    // 현재 구역(노드)에 속한 플레이어들의 위치 정보
    private readonly Dictionary<string, PlayerPosition> _players = new();
    
    private IDisposable? _timer = null;

    public WorldGrain(IConnection natsConnection, ILogger<WorldGrain> logger)
    {
        _natsConnection = natsConnection;
        _logger = logger;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        this.RegisterGrainTimer<object?>(callback: this.Broadcast, state: null, options: new GrainTimerCreationOptions
        {
            DueTime = TimeSpan.FromMilliseconds(100),
            Period = TimeSpan.FromMilliseconds(100),
            Interleave = false,
            KeepAlive = false
        });
        
        _logger.LogInformation($"WorldGrain {this.GetPrimaryKeyString()} activated and timer started.");
        return base.OnActivateAsync(cancellationToken);
    }
    
    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _logger.LogInformation($"WorldGrain {this.GetPrimaryKeyString()} deactivated.");
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    private Task Broadcast(object? state)
    {
        if (_players.Count == 0)
        {
            return Task.CompletedTask;
        }

        var response = new SubscribeResponse();
        response.PlayerPositionList.AddRange(_players.Values);
        var worldEvent = new WorldEvent { Positions = response };
        var topic = $"world.{this.GetPrimaryKeyString()}.updates";
        var data = worldEvent.ToByteArray();

        try
        {
            _natsConnection.Publish(topic, data);            
        }
        catch (Exception e)
        {
            this._logger.LogError(e, "failed to publish world Event");
            throw new Exception($"failed to publish world event {e.Message}");
        }
        
        return Task.CompletedTask;
    }

    public Task Enter(PlayerPosition position)
    {
        _players[position.PlayerId] = position;
        _logger.LogInformation($"Player {position.PlayerId} entered grain {this.GetPrimaryKeyString()}");
        return Task.CompletedTask;
    }

    public Task Leave(string playerId)
    {
        if (_players.Remove(playerId))
        {
            _logger.LogInformation($"Player {playerId} left grain {this.GetPrimaryKeyString()}");

            var playerLeft = new PlayerLeft { PlayerId = playerId };
            var worldEvent = new WorldEvent { PlayerLeft = playerLeft };
            
            var topic = $"world.{this.GetPrimaryKeyString()}.updates";
            var data = worldEvent.ToByteArray();

            try
            {
                _natsConnection.Publish(topic, data);
            }
            catch (Exception e)
            {
                this._logger.LogError(e, "failed to publish world Event");
                throw new Exception($"failed to publish world event {e.Message}");
            }
        }
        
        return Task.CompletedTask;
    }

    public Task UpdatePlayerPosition(PlayerPosition position)
    {
        _players[position.PlayerId] = position;
        return Task.CompletedTask;
    }

    public Task<List<PlayerPosition>> GetPlayers()
    {
        return Task.FromResult(_players.Values.ToList());
    }
}