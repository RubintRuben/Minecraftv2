using System;
using UnityEngine;

public static class ChunkFeatureGenerator
{
    private const int TreeEdgePadding = 2;

    public static void Generate(Chunk c)
    {
        GenerateOres(c);
        GenerateTreesLikeBeforeButTopLeaf(c);
        GenerateShrubs(c);
    }

    // -----------------------
    // ORES
    // -----------------------
    private static void GenerateOres(Chunk c)
    {
        var w = c.World;
        w.GetOreSettings(out var o);

        int chunkSeed = w.Hash3(c.Coord.x, 0, c.Coord.y);
        System.Random rng = new System.Random(chunkSeed);

        PlaceVeins(c, rng, BlockType.CoalOre, o.coal);
        PlaceVeins(c, rng, BlockType.IronOre, o.iron);
        PlaceVeins(c, rng, BlockType.GoldOre, o.gold);
        PlaceVeins(c, rng, BlockType.LapisOre, o.lapis, triangularY: true);
        PlaceVeins(c, rng, BlockType.RedstoneOre, o.redstone);
        PlaceVeins(c, rng, BlockType.DiamondOre, o.diamond);

        int avgSurface = EstimateChunkAverageSurface(c);
        if (avgSurface >= VoxelWorld.SeaLevel + 22)
            PlaceVeins(c, rng, BlockType.EmeraldOre, o.emerald, emeraldScatter: true);
    }

    private static int EstimateChunkAverageSurface(Chunk c)
    {
        var w = c.World;
        int sum = 0;
        int cnt = 0;

        for (int x = 0; x < VoxelData.ChunkSize; x += 4)
        for (int z = 0; z < VoxelData.ChunkSize; z += 4)
        {
            int wx = c.Coord.x * VoxelData.ChunkSize + x;
            int wz = c.Coord.y * VoxelData.ChunkSize + z;
            sum += w.GetLandHeight(wx, wz);
            cnt++;
        }

        return (cnt == 0) ? VoxelWorld.SeaLevel : (sum / cnt);
    }

    private static void PlaceVeins(
        Chunk c,
        System.Random rng,
        BlockType oreType,
        VoxelWorld.OreLayer layer,
        bool triangularY = false,
        bool emeraldScatter = false)
    {
        for (int i = 0; i < layer.countPerChunk; i++)
        {
            int x = rng.Next(0, VoxelData.ChunkSize);
            int z = rng.Next(0, VoxelData.ChunkSize);

            int y;
            if (triangularY)
            {
                int a = rng.Next(layer.yMin, layer.yMax + 1);
                int b = rng.Next(layer.yMin, layer.yMax + 1);
                y = (a + b) / 2;
            }
            else
            {
                y = rng.Next(layer.yMin, Mathf.Min(layer.yMax + 1, VoxelData.ChunkHeight));
            }

            if (y < 1 || y >= VoxelData.ChunkHeight - 1) continue;

            if (emeraldScatter)
            {
                if (c.GetBlockLocal(x, y, z) == BlockType.Stone)
                    c.SetBlockLocal(x, y, z, oreType);
                continue;
            }

            int veinSize = Mathf.Max(1, layer.veinSize);
            int vx = x, vy = y, vz = z;

            for (int v = 0; v < veinSize; v++)
            {
                if (vx < 1 || vx >= VoxelData.ChunkSize - 1) break;
                if (vz < 1 || vz >= VoxelData.ChunkSize - 1) break;
                if (vy < 1 || vy >= VoxelData.ChunkHeight - 1) break;

                if (c.GetBlockLocal(vx, vy, vz) == BlockType.Stone)
                    c.SetBlockLocal(vx, vy, vz, oreType);

                int dir = rng.Next(0, 6);
                switch (dir)
                {
                    case 0: vx++; break;
                    case 1: vx--; break;
                    case 2: vz++; break;
                    case 3: vz--; break;
                    case 4: vy++; break;
                    case 5: vy--; break;
                }
            }
        }
    }

    // -----------------------
    // TREES (mint eddig, csak top közép leaf)
    // és a "nagy fa": 1 vastag törzs + feljebb ágak
    // -----------------------
    private static void GenerateTreesLikeBeforeButTopLeaf(Chunk c)
    {
        var w = c.World;

        for (int x = TreeEdgePadding; x < VoxelData.ChunkSize - TreeEdgePadding; x++)
        {
            for (int z = TreeEdgePadding; z < VoxelData.ChunkSize - TreeEdgePadding; z++)
            {
                int wx = c.Coord.x * VoxelData.ChunkSize + x;
                int wz = c.Coord.y * VoxelData.ChunkSize + z;

                w.GetBiomeParams(wx, wz, out _, out float rain, out float forest, out float ocean);
                if (ocean >= w.oceanThreshold) continue;

                int surfaceY = w.GetLandHeight(wx, wz);
                if (surfaceY <= VoxelWorld.SeaLevel + 1) continue;
                if (surfaceY < 1 || surfaceY >= VoxelData.ChunkHeight - 12) continue;

                if (c.GetBlockLocal(x, surfaceY, z) != BlockType.Grass) continue;
                if (c.GetBlockLocal(x, surfaceY + 1, z) != BlockType.Air) continue;

                bool isForest = forest > 0.50f;
                float humidityFactor = Mathf.Lerp(0.70f, 1.25f, rain);
                float chance = isForest ? (0.090f * humidityFactor) : (0.010f * humidityFactor);

                int h = w.Hash3(wx, surfaceY, wz);
                float r01 = (Mathf.Abs(h) % 10000) / 10000f;
                if (r01 > chance) continue;

                float bigChance = isForest ? 0.14f : 0.04f;
                float r02 = (Mathf.Abs(w.Hash3(wx + 17, surfaceY + 3, wz - 9)) % 10000) / 10000f;
                bool big = r02 < bigChance;

                if (big)
                    TryPlaceBigOakSingleTrunkWithBranches(c, x, surfaceY, z, wx, wz);
                else
                    TryPlaceSmallOakLikeBeforeTopLeaf(c, x, surfaceY, z, wx, wz);
            }
        }
    }

    private static bool TryPlaceSmallOakLikeBeforeTopLeaf(Chunk c, int x, int surfaceY, int z, int wx, int wz)
    {
        var w = c.World;

        int trunkH = 4 + (Mathf.Abs(w.Hash3(wx, surfaceY, wz)) % 3); // 4..6
        int topLogY = surfaceY + trunkH - 1;  // idáig log
        int topLeafY = surfaceY + trunkH;     // legfelül közép LEAF

        if (topLeafY + 2 >= VoxelData.ChunkHeight) return false;

        // hely
        for (int y = surfaceY + 1; y <= topLeafY; y++)
            if (c.GetBlockLocal(x, y, z) != BlockType.Air) return false;

        // törzs
        for (int y = surfaceY + 1; y <= topLogY; y++)
            c.SetBlockLocal(x, y, z, BlockType.Log);

        // levelek rétegek (mint a régi kód “square” stílusa)
        PlaceLeavesSquare(c, x, z, topLeafY - 1, 2, false);
        PlaceLeavesSquare(c, x, z, topLeafY, 2, true);
        PlaceLeavesSquare(c, x, z, topLeafY + 1, 1, true);
        PlaceLeavesCross(c, x, z, topLeafY + 2);

        // FIX: legfelső közép mindig LEAF
        if (c.GetBlockLocal(x, topLeafY, z) == BlockType.Air)
            c.SetBlockLocal(x, topLeafY, z, BlockType.Leaves);

        return true;
    }

    private static bool TryPlaceBigOakSingleTrunkWithBranches(Chunk c, int x, int surfaceY, int z, int wx, int wz)
    {
        var w = c.World;

        int trunkH = 8 + (Mathf.Abs(w.Hash3(wx, surfaceY, wz)) % 5); // 8..12
        int topLogY = surfaceY + trunkH - 2; // NE legyen a teteje log
        int topLeafY = surfaceY + trunkH;    // legfelül leaf

        if (topLeafY + 3 >= VoxelData.ChunkHeight) return false;

        // hely (törzs)
        for (int y = surfaceY + 1; y <= topLeafY; y++)
            if (c.GetBlockLocal(x, y, z) != BlockType.Air) return false;

        // törzs
        for (int y = surfaceY + 1; y <= topLogY; y++)
            c.SetBlockLocal(x, y, z, BlockType.Log);

        // lombkorona (minecraftosabb: nagyobb rétegek fent)
        PlaceLeavesSquare(c, x, z, topLeafY - 3, 3, false);
        PlaceLeavesSquare(c, x, z, topLeafY - 2, 3, true);
        PlaceLeavesSquare(c, x, z, topLeafY - 1, 2, true);
        PlaceLeavesSquare(c, x, z, topLeafY, 2, true);
        PlaceLeavesSquare(c, x, z, topLeafY + 1, 1, true);
        PlaceLeavesCross(c, x, z, topLeafY + 2);

        // FIX: legfelső közép mindig LEAF
        if (c.GetBlockLocal(x, topLeafY, z) == BlockType.Air)
            c.SetBlockLocal(x, topLeafY, z, BlockType.Leaves);

        // ágak: 1 vastag törzs marad, de feljebb oldalsó logok
        int seed = w.Hash3(wx + 123, surfaceY + 77, wz - 55);
        System.Random rng = new System.Random(seed);

        int branches = 2 + rng.Next(0, 3);
        for (int i = 0; i < branches; i++)
        {
            int by = topLeafY - 2 - rng.Next(0, 3);
            int dir = rng.Next(0, 4);
            int dx = (dir == 0) ? 1 : (dir == 1) ? -1 : 0;
            int dz = (dir == 2) ? 1 : (dir == 3) ? -1 : 0;
            int len = 2 + rng.Next(0, 3);

            int ax = x;
            int az = z;

            for (int s = 0; s < len; s++)
            {
                ax += dx;
                az += dz;
                if (ax < 1 || ax >= VoxelData.ChunkSize - 1) break;
                if (az < 1 || az >= VoxelData.ChunkSize - 1) break;
                if (by < 1 || by >= VoxelData.ChunkHeight - 1) break;

                if (c.GetBlockLocal(ax, by, az) == BlockType.Air)
                    c.SetBlockLocal(ax, by, az, BlockType.Log);

                PlaceLeavesSquare(c, ax, az, by + 1, 1, true);
                if (c.GetBlockLocal(ax, by + 2, az) == BlockType.Air)
                    c.SetBlockLocal(ax, by + 2, az, BlockType.Leaves);
            }
        }

        return true;
    }

    private static void PlaceLeavesSquare(Chunk c, int cx, int cz, int y, int r, bool allowCorners)
    {
        if (y < 0 || y >= VoxelData.ChunkHeight) return;

        for (int dx = -r; dx <= r; dx++)
        for (int dz = -r; dz <= r; dz++)
        {
            if (!allowCorners && Mathf.Abs(dx) == r && Mathf.Abs(dz) == r) continue;

            int ax = cx + dx;
            int az = cz + dz;
            if (ax < 0 || ax >= VoxelData.ChunkSize) continue;
            if (az < 0 || az >= VoxelData.ChunkSize) continue;

            if (c.GetBlockLocal(ax, y, az) == BlockType.Air)
                c.SetBlockLocal(ax, y, az, BlockType.Leaves);
        }
    }

    private static void PlaceLeavesCross(Chunk c, int cx, int cz, int y)
    {
        if (y < 0 || y >= VoxelData.ChunkHeight) return;

        int[,] pts = new int[,]
        {
            {0,0},{1,0},{-1,0},{0,1},{0,-1}
        };

        for (int i = 0; i < pts.GetLength(0); i++)
        {
            int ax = cx + pts[i, 0];
            int az = cz + pts[i, 1];
            if (ax < 0 || ax >= VoxelData.ChunkSize) continue;
            if (az < 0 || az >= VoxelData.ChunkSize) continue;

            if (c.GetBlockLocal(ax, y, az) == BlockType.Air)
                c.SetBlockLocal(ax, y, az, BlockType.Leaves);
        }
    }

    // -----------------------
    // SHRUBS (marad kb ugyanaz)
    // -----------------------
    private static void GenerateShrubs(Chunk c)
    {
        var w = c.World;

        int wx0 = c.Coord.x * VoxelData.ChunkSize;
        int wz0 = c.Coord.y * VoxelData.ChunkSize;

        for (int x = 1; x < VoxelData.ChunkSize - 1; x++)
        for (int z = 1; z < VoxelData.ChunkSize - 1; z++)
        {
            int wx = wx0 + x;
            int wz = wz0 + z;

            w.GetBiomeParams(wx, wz, out _, out float rain, out float forest, out float ocean);
            if (ocean >= w.oceanThreshold) continue;

            int surfaceY = w.GetLandHeight(wx, wz);
            if (surfaceY <= VoxelWorld.SeaLevel + 1) continue;
            if (surfaceY < 1 || surfaceY >= VoxelData.ChunkHeight - 6) continue;
            if (c.GetBlockLocal(x, surfaceY, z) != BlockType.Grass) continue;

            bool isForest = forest > 0.50f;
            if (!isForest) continue;

            float patch = Mathf.PerlinNoise((wx + w.settings.seed * 19) * 0.08f, (wz + w.settings.seed * 19) * 0.08f);
            float density = Mathf.Lerp(0.22f, 0.46f, rain) * Mathf.Lerp(0.65f, 1.25f, patch);

            int h = w.Hash3(wx, surfaceY + 9, wz);
            float r = (Mathf.Abs(h) % 10000) / 10000f;
            if (r > density) continue;

            int y1 = surfaceY + 1;
            if (y1 < 1 || y1 >= VoxelData.ChunkHeight - 1) continue;
            if (c.GetBlockLocal(x, y1, z) != BlockType.Air) continue;

            int kind = Mathf.Abs(w.Hash3(wx + 3, surfaceY + 1, wz + 7)) % 100;

            if (kind < 70)
            {
                c.SetBlockLocal(x, y1, z, BlockType.Leaves);
                if (kind < 22)
                {
                    int y2 = y1 + 1;
                    if (y2 < VoxelData.ChunkHeight - 1 && c.GetBlockLocal(x, y2, z) == BlockType.Air)
                        c.SetBlockLocal(x, y2, z, BlockType.Leaves);
                }
            }
            else
            {
                c.SetBlockLocal(x, y1, z, BlockType.Log);
                int y2 = y1 + 1;
                if (y2 < VoxelData.ChunkHeight - 1 && c.GetBlockLocal(x, y2, z) == BlockType.Air)
                    c.SetBlockLocal(x, y2, z, BlockType.Leaves);

                PlaceLeavesSquare(c, x, z, y2, 1, true);
            }
        }
    }
}
