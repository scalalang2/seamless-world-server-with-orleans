using GameProtocol;
using GameProtocol.Grains;
using Orleans;

namespace GameLogicServer.Grains;

public class PlayerGrain : Grain, IPlayerGrain
{
    private PlayerPosition _position = new();

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _position.PlayerId = this.GetPrimaryKeyString();
        return base.OnActivateAsync(cancellationToken);
    }
    
    public Task UpdatePosition(PlayerPosition position)
    {
        _position = position;
        return Task.CompletedTask;
    }

    public Task<PlayerPosition> GetPosition()
    {
        return Task.FromResult(_position);
    }

    public Task<string> GetFieldId()
    {
        // Player has no position yet
        if (_position.X == 0 && _position.Y == 0 && _position.Z == 0)
        {
            return Task.FromResult(string.Empty);
        }
        
        return Task.FromResult(QuadTreeHelper.GetNodeIdForPosition(_position));
    }
}