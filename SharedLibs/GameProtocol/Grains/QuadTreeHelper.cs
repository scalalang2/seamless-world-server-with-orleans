
using System.Text;

namespace GameProtocol.Grains;

public static class QuadTreeHelper
{
    private const double MinX = -213.0;
    private const double MaxX = 336.0;
    private const double MinZ = -235.0;
    private const double MaxZ = 177.0;
    private const int MaxLevel = 4; // PlayerGrain의 레벨과 일치해야 함

    /// <summary>
    /// 주어진 위치에 해당하는 QuadTree 노드의 ID를 계산합니다.
    /// </summary>
    public static string GetNodeIdForPosition(PlayerPosition position)
    {
        var sb = new StringBuilder("0");
        var (minX, maxX, minZ, maxZ) = (MinX, MaxX, MinZ, MaxZ);

        for (var level = 1; level <= MaxLevel; level++)
        {
            var midX = minX + (maxX - minX) / 2.0;
            var midZ = minZ + (maxZ - minZ) / 2.0;

            var quadrant = 0;
            if (position.X < midX)
            {
                if (position.Z < midZ) { quadrant = 0; maxX = midX; maxZ = midZ; } // 좌상단
                else { quadrant = 2; maxX = midX; minZ = midZ; } // 좌하단
            }
            else
            {
                if (position.Z < midZ) { quadrant = 1; minX = midX; maxZ = midZ; } // 우상단
                else { quadrant = 3; minX = midX; minZ = midZ; } // 우하단
            }
            sb.Append($"-{quadrant}");
        }

        return sb.ToString();
    }
    
    /// <summary>
    /// 지정된 노드와 이웃 노드들의 ID 목록을 반환합니다.
    /// </summary>
    public static List<string> GetNeighborIds(string nodeId, int aoiLevel)
    {
        if (aoiLevel == 0)
        {
            return new List<string> { nodeId };
        }
        
        // 현재는 aoiLevel 1만 지원
        var bounds = GetNodeBounds(nodeId);
        var width = bounds.maxX - bounds.minX;
        var height = bounds.maxZ - bounds.minZ;

        // 8방향 + 중앙 지점
        var testPoints = new List<PlayerPosition>
        {
            new() { X = bounds.midX, Z = bounds.midZ }, // 중앙
            new() { X = bounds.midX, Z = bounds.midZ - height }, // 위
            new() { X = bounds.midX, Z = bounds.midZ + height }, // 아래
            new() { X = bounds.midX - width, Z = bounds.midZ }, // 왼쪽
            new() { X = bounds.midX + width, Z = bounds.midZ }, // 오른쪽
            new() { X = bounds.midX - width, Z = bounds.midZ - height }, // Up-Left
            new() { X = bounds.midX + width, Z = bounds.midZ - height }, // Up-Right
            new() { X = bounds.midX - width, Z = bounds.midZ + height }, // Down-Left
            new() { X = bounds.midX + width, Z = bounds.midZ + height }  // Down-Right
        };

        var neighborIds = new HashSet<string>();
        foreach (var point in testPoints)
        {
            neighborIds.Add(GetNodeIdForPosition(point));
        }

        return neighborIds.ToList();
    }

    /// <summary>
    /// 노드 ID에 해당하는 월드 좌표 경계를 계산합니다.
    /// </summary>
    private static (double minX, double maxX, double minZ, double maxZ, double midX, double midZ) GetNodeBounds(string nodeId)
    {
        var (minX, maxX, minZ, maxZ) = (MinX, MaxX, MinZ, MaxZ);

        var path = nodeId.Split('-').Skip(1).Select(int.Parse);

        foreach (var quadrant in path)
        {
            var midX = minX + (maxX - minX) / 2.0;
            var midZ = minZ + (maxZ - minZ) / 2.0;

            switch (quadrant)
            {
                case 0: // 좌상단
                    maxX = midX;
                    maxZ = midZ;
                    break;
                case 1: // 우상단
                    minX = midX;
                    maxZ = midZ;
                    break;
                case 2: // 좌하단
                    maxX = midX;
                    minZ = midZ;
                    break;
                case 3: // 우하단
                    minX = midX;
                    minZ = midZ;
                    break;
            }
        }
        
        var finalMidX = minX + (maxX - minX) / 2.0;
        var finalMidZ = minZ + (maxZ - minZ) / 2.0;

        return (minX, maxX, minZ, maxZ, finalMidX, finalMidZ);
    }
}
