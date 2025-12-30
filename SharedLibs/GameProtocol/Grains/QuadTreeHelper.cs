
using System.Text;

namespace GameProtocol.Grains;

public static class QuadTreeHelper
{
    /// <summary>
    /// 주어진 위치에 해당하는 QuadTree 노드의 ID를 계산합니다.
    /// </summary>
    public static string GetNodeIdForPosition(PlayerPosition position)
    {
        var sb = new StringBuilder("0");
        var (minX, maxX, minZ, maxZ) = (
            Constants.Constants.MinAreaX, Constants.Constants.MaxAreaX, 
            Constants.Constants.MinAreaZ, Constants.Constants.MaxAreaZ);

        for (var level = 1; level <= Constants.Constants.MaxQuaudLevel; level++)
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
        
        // TODO: aoiLevel이 현재 미적용 되어 있음
        var bounds = GetNodeBounds(nodeId);
        var width = bounds.maxX - bounds.minX;
        var height = bounds.maxZ - bounds.minZ;

        var neighborIds = new HashSet<string>();

        for (int xOffset = -aoiLevel; xOffset <= aoiLevel; xOffset++)
        {
            for (int zOffset = -aoiLevel; zOffset <= aoiLevel; zOffset++)
            {
                var testX = bounds.midX + xOffset * width;
                var testZ = bounds.midZ + zOffset * height;
                
                neighborIds.Add(GetNodeIdForPosition(new PlayerPosition { X = testX, Y = 0, Z = testZ }));
            }
        }

        return neighborIds.ToList();
    }

    /// <summary>
    /// 노드 ID에 해당하는 월드 좌표 경계를 계산합니다.
    /// </summary>
    private static (double minX, double maxX, double minZ, double maxZ, double midX, double midZ) GetNodeBounds(string nodeId)
    {
        var (minX, maxX, minZ, maxZ) = (
            Constants.Constants.MinAreaX, Constants.Constants.MaxAreaX, 
            Constants.Constants.MinAreaZ, Constants.Constants.MaxAreaZ);


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
