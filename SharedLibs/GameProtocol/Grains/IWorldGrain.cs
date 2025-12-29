using Orleans;

namespace GameProtocol.Grains;

// 월드의 한 구역(QuadTree 노드)을 관리하는 Grain입니다.
public interface IWorldGrain : IGrainWithStringKey
{
    // 플레이어가 이 구역에 진입했음을 알립니다.
    Task Enter(PlayerPosition position);

    // 플레이어가 이 구역을 떠났음을 알립니다.
    Task Leave(string playerId);

    // 구역 내에서 플레이어의 위치를 갱신합니다.
    Task UpdatePlayerPosition(PlayerPosition position);
    
    // 현재 구역 내의 모든 플레이어 목록을 가져옵니다.
    Task<List<PlayerPosition>> GetPlayers();
}
