using Orleans;

namespace GameProtocol.Grains;

// 플레이어를 나타내는 Grain입니다.
public interface IPlayerGrain : IGrainWithStringKey
{
    // 클라이언트로부터 위치 정보를 받아서 갱신합니다.
    Task UpdatePosition(PlayerPosition position);
    
    // 현재 플레이어의 위치 정보를 가져옵니다.
    Task<PlayerPosition> GetPosition();
    
    // 현재 플레이어가 속한 필드(quad)의 ID를 가져옵니다.
    Task<string> GetFieldId();
}