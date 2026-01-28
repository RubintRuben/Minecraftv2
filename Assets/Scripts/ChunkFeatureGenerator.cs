using UnityEngine;

public static class ChunkFeatureGenerator
{
    public static void Generate(Chunk c)
    {
        var w = c.World;

        CarveCaves(c, w);
        GenerateOres(c, w);
        GenerateClayPatches(c, w);
        GenerateSurfaceFloraAndTrees(c, w);
    }

    static void CarveCaves(Chunk c, VoxelWorld w)
    {
        int seed = w.Hash3(c.Coord.x, 98765, c.Coord.y);
        System.Random rng = new System.Random(seed);

        int caveCount = rng.Next(1, 4); 

        for (int i = 0; i < caveCount; i++)
        {
            double startX = rng.NextDouble() * 16.0;
            double startZ = rng.NextDouble() * 16.0;
            double startY = rng.Next(10, 60);

            double yaw = rng.NextDouble() * Mathf.PI * 2;
            double pitch = (rng.NextDouble() - 0.5) * 0.5;

            int length = rng.Next(80, 150);
            double radius = rng.NextDouble() * 1.5 + 1.5;

            WormCave(c, w, rng, startX, startY, startZ, yaw, pitch, length, radius);
        }
    }

    static void WormCave(Chunk c, VoxelWorld w, System.Random rng, double x, double y, double z, double yaw, double pitch, int len, double rad)
    {
        double yawDelta = 0;
        double pitchDelta = 0;

        for (int i = 0; i < len; i++)
        {
            x += System.Math.Cos(yaw) * System.Math.Cos(pitch);
            z += System.Math.Sin(yaw) * System.Math.Cos(pitch);
            y += System.Math.Sin(pitch);

            yawDelta += (rng.NextDouble() - 0.5) * 0.1;
            pitchDelta += (rng.NextDouble() - 0.5) * 0.1;
            yaw += yawDelta;
            pitch += pitchDelta;
            pitch = Mathf.Clamp((float)pitch, -1.0f, 1.0f);

            yawDelta *= 0.9;
            pitchDelta *= 0.9;

            if (rng.Next(50) == 0)
            {
                WormCave(c, w, rng, x, y, z, rng.NextDouble() * Mathf.PI * 2, (rng.NextDouble() - 0.5) * 0.5, len / 2, rad * 0.8);
            }

            int r = Mathf.CeilToInt((float)rad);
            int minX = Mathf.FloorToInt((float)x - r);
            int maxX = Mathf.FloorToInt((float)x + r);
            int minY = Mathf.FloorToInt((float)y - r);
            int maxY = Mathf.FloorToInt((float)y + r);
            int minZ = Mathf.FloorToInt((float)z - r);
            int maxZ = Mathf.FloorToInt((float)z + r);

            for (int xx = minX; xx <= maxX; xx++)
            for (int yy = minY; yy <= maxY; yy++)
            for (int zz = minZ; zz <= maxZ; zz++)
            {
                double dx = xx + 0.5 - x;
                double dy = yy + 0.5 - y;
                double dz = zz + 0.5 - z;
                if (dx * dx + dy * dy + dz * dz < rad * rad)
                {
                    BlockType current = c.GetBlockLocal(xx, yy, zz);
                    if (current == BlockType.Stone || current == BlockType.Dirt || current == BlockType.Gravel || current == BlockType.Sand || current == BlockType.Clay)
                    {
                        if (yy < 8) c.SetBlockLocal(xx, yy, zz, BlockType.Water);
                        else c.SetBlockLocal(xx, yy, zz, BlockType.Air);
                    }
                }
            }
        }
    }

    static void GenerateOres(Chunk c, VoxelWorld w)
    {
        int seed = w.Hash3(c.Coord.x, 71003, c.Coord.y);
        System.Random rng = new System.Random(seed);

        SpawnOreVeins(c, rng, BlockType.CoalOre, 16, 14, 0, 127);
        SpawnOreVeins(c, rng, BlockType.IronOre, 14, 8, 0, 60);
        SpawnOreVeins(c, rng, BlockType.GoldOre, 4, 8, 0, 30);
        SpawnOreVeins(c, rng, BlockType.RedstoneOre, 8, 7, 0, 15);
        SpawnOreVeins(c, rng, BlockType.DiamondOre, 2, 7, 0, 14);
        SpawnOreVeins(c, rng, BlockType.LapisOre, 2, 6, 0, 30);
        SpawnOreVeins(c, rng, BlockType.Gravel, 8, 18, 0, 127);
        SpawnOreVeins(c, rng, BlockType.Dirt, 12, 22, 0, 127);
    }

    static void SpawnOreVeins(Chunk c, System.Random rng, BlockType ore, int attempts, int size, int yMin, int yMax)
    {
        for (int i = 0; i < attempts; i++)
        {
            int x = rng.Next(0, 16);
            int z = rng.Next(0, 16);
            int y = rng.Next(yMin, yMax + 1);

            float angle = (float)rng.NextDouble() * Mathf.PI;
            float dx = Mathf.Sin(angle);
            float dz = Mathf.Cos(angle);

            float len = size / 8f;
            
            for(int s=0; s<size; s++) {
                float t = s / (float)size;
                int bx = Mathf.RoundToInt(x + dx * t * len);
                int bz = Mathf.RoundToInt(z + dz * t * len);
                // JAVÍTÁS: (float) castolás a double kifejezésre
                int by = Mathf.RoundToInt((float)(y + (rng.NextDouble()-0.5)*2)); 

                if (c.GetBlockLocal(bx, by, bz) == BlockType.Stone)
                {
                     c.SetBlockLocal(bx, by, bz, ore);
                }
            }
        }
    }

    static void GenerateClayPatches(Chunk c, VoxelWorld w)
    {
        int seed = w.Hash3(c.Coord.x, 88001, c.Coord.y);
        System.Random rng = new System.Random(seed);

        for (int i = 0; i < 4; i++)
        {
            int x = rng.Next(0, 16);
            int z = rng.Next(0, 16);
            int wx = c.Coord.x * 16 + x;
            int wz = c.Coord.y * 16 + z;

            int h = w.GetLandHeight(wx, wz);
            if (h > VoxelWorld.SeaLevel - 1) continue;

            int y = Mathf.Clamp(h, 2, VoxelWorld.SeaLevel);
            BlockType baseT = c.GetBlockLocal(x, y, z);
            
            if (baseT == BlockType.Dirt || baseT == BlockType.Gravel || baseT == BlockType.Sand)
            {
                c.SetBlockLocal(x, y, z, BlockType.Clay);
                if (rng.Next(2) == 0) c.SetBlockLocal(x+1, y, z, BlockType.Clay);
                if (rng.Next(2) == 0) c.SetBlockLocal(x-1, y, z, BlockType.Clay);
                if (rng.Next(2) == 0) c.SetBlockLocal(x, y, z+1, BlockType.Clay);
                if (rng.Next(2) == 0) c.SetBlockLocal(x, y, z-1, BlockType.Clay);
            }
        }
    }

    static void GenerateSurfaceFloraAndTrees(Chunk c, VoxelWorld w)
    {
        int baseX = c.Coord.x * 16;
        int baseZ = c.Coord.y * 16;

        for (int x = 2; x < 14; x++)
        for (int z = 2; z < 14; z++)
        {
            int wx = baseX + x;
            int wz = baseZ + z;

            int y = w.GetLandHeight(wx, wz);
            if (y <= VoxelWorld.SeaLevel || y >= 126) continue;

            BlockType ground = c.GetBlockLocal(x, y, z);
            var b = w.GetBiome(wx, wz);

            if (ground == BlockType.Grass)
            {
                if (HasNearbyLog(c, x, y + 1, z, 3)) continue;

                if (b == VoxelWorld.BiomeId.Forest)
                {
                    if (w.RandomChance(wx, y, wz, 22, 1)) PlaceOakCustom(c, x, y, z, w, rngForTree(wx, y, wz));
                }
                else if (b == VoxelWorld.BiomeId.BirchForest)
                {
                    if (w.RandomChance(wx, y, wz, 18, 1)) PlaceBirchCustom(c, x, y, z, w, rngForTree(wx, y, wz));
                }
                else if (b == VoxelWorld.BiomeId.Taiga)
                {
                    if (w.RandomChance(wx, y, wz, 18, 1)) PlaceSpruce(c, x, y, z, w);
                }
                else if (b == VoxelWorld.BiomeId.Jungle)
                {
                    if (w.RandomChance(wx, y, wz, 12, 1)) PlaceJungle(c, x, y, z, w);
                }
                else if (b == VoxelWorld.BiomeId.Savanna)
                {
                    if (w.RandomChance(wx, y, wz, 20, 1)) PlaceAcacia(c, x, y, z, w);
                }
                else
                {
                    if (w.RandomChance(wx, y, wz, 70, 1))
                    {
                        if (w.RandomChance(wx, y, wz, 10, 1)) PlaceBigOakCustom(c, x, y, z, w, rngForTree(wx, y, wz));
                        else PlaceOakCustom(c, x, y, z, w, rngForTree(wx, y, wz));
                    }
                }
            }
            else if (ground == BlockType.Sand)
            {
                if (b == VoxelWorld.BiomeId.Desert)
                {
                    if (w.RandomChance(wx, y, wz, 22, 1)) PlaceCactus(c, x, y, z, w);
                    else if (w.RandomChance(wx, y, wz, 16, 1)) c.SetBlockLocal(x, y + 1, z, BlockType.DeadBush);
                }
            }
        }
    }

    static System.Random rngForTree(int x, int y, int z) => new System.Random(x * 10000 + y * 100 + z);

    static void PlaceOakCustom(Chunk c, int x, int y, int z, VoxelWorld w, System.Random rng)
    {
        int h = 5 + rng.Next(0, 3);
        
        for (int i = 1; i <= h; i++) SetL(c, x, y + i, z, BlockType.OakLog);

        int leavesStart = h - 2;
        
        for (int ly = leavesStart; ly <= h + 1; ly++)
        {
            int relY = ly - leavesStart; 
            
            if (relY == 0 || relY == 1) 
            {
                for (int lx = -2; lx <= 2; lx++)
                for (int lz = -2; lz <= 2; lz++)
                {
                    if (Mathf.Abs(lx) == 2 && Mathf.Abs(lz) == 2)
                    {
                        if (rng.Next(0, 2) == 0) continue;
                    }
                    if (lx == 0 && lz == 0) continue; 
                    SetL(c, x + lx, y + ly + 1, z + lz, BlockType.OakLeaves);
                }
            }
            else if (relY == 2) 
            {
                for (int lx = -1; lx <= 1; lx++)
                for (int lz = -1; lz <= 1; lz++)
                {
                    if (Mathf.Abs(lx) == 1 && Mathf.Abs(lz) == 1)
                    {
                        if (rng.Next(0, 2) == 0) continue;
                    }
                    if (lx == 0 && lz == 0) continue;
                    SetL(c, x + lx, y + ly + 1, z + lz, BlockType.OakLeaves);
                }
            }
            else 
            {
                SetL(c, x, y + ly + 1, z, BlockType.OakLeaves);
                SetL(c, x + 1, y + ly + 1, z, BlockType.OakLeaves);
                SetL(c, x - 1, y + ly + 1, z, BlockType.OakLeaves);
                SetL(c, x, y + ly + 1, z + 1, BlockType.OakLeaves);
                SetL(c, x, y + ly + 1, z - 1, BlockType.OakLeaves);
            }
        }
    }

    static void PlaceBirchCustom(Chunk c, int x, int y, int z, VoxelWorld w, System.Random rng)
    {
        int h = 5 + rng.Next(0, 3);
        
        for (int i = 1; i <= h; i++) SetL(c, x, y + i, z, BlockType.BirchLog);

        int leavesStart = h - 2;
        
        for (int ly = leavesStart; ly <= h + 1; ly++)
        {
            int relY = ly - leavesStart; 
            
            if (relY == 0 || relY == 1) 
            {
                for (int lx = -2; lx <= 2; lx++)
                for (int lz = -2; lz <= 2; lz++)
                {
                    if (Mathf.Abs(lx) == 2 && Mathf.Abs(lz) == 2)
                    {
                        if (rng.Next(0, 2) == 0) continue;
                    }
                    if (lx == 0 && lz == 0) continue;
                    SetL(c, x + lx, y + ly + 1, z + lz, BlockType.BirchLeaves);
                }
            }
            else if (relY == 2) 
            {
                for (int lx = -1; lx <= 1; lx++)
                for (int lz = -1; lz <= 1; lz++)
                {
                    if (Mathf.Abs(lx) == 1 && Mathf.Abs(lz) == 1)
                    {
                        if (rng.Next(0, 2) == 0) continue;
                    }
                    if (lx == 0 && lz == 0) continue;
                    SetL(c, x + lx, y + ly + 1, z + lz, BlockType.BirchLeaves);
                }
            }
            else 
            {
                SetL(c, x, y + ly + 1, z, BlockType.BirchLeaves);
                SetL(c, x + 1, y + ly + 1, z, BlockType.BirchLeaves);
                SetL(c, x - 1, y + ly + 1, z, BlockType.BirchLeaves);
                SetL(c, x, y + ly + 1, z + 1, BlockType.BirchLeaves);
                SetL(c, x, y + ly + 1, z - 1, BlockType.BirchLeaves);
            }
        }
    }

    static void PlaceBigOakCustom(Chunk c, int x, int y, int z, VoxelWorld w, System.Random rng)
    {
        int h = 8 + rng.Next(0, 7); 
        for (int i = 1; i <= h; i++) SetL(c, x, y + i, z, BlockType.OakLog);

        int branchCount = 4 + rng.Next(0, 3);
        for(int k=0; k<branchCount; k++)
        {
            int by = y + h - 3 - rng.Next(0, 4);
            int dirX = rng.Next(-1, 2);
            int dirZ = rng.Next(-1, 2);
            if (dirX == 0 && dirZ == 0) dirX = 1;

            int branchLen = 3 + rng.Next(0, 3);
            for(int b=1; b<=branchLen; b++)
            {
                SetL(c, x + dirX*b, by + b/2, z + dirZ*b, BlockType.OakLog);
            }
            PlaceLeafBlob(c, x + dirX*branchLen, by + branchLen/2, z + dirZ*branchLen, BlockType.OakLeaves);
        }

        PlaceLeafBlob(c, x, y + h + 1, z, BlockType.OakLeaves);
    }

    static bool HasNearbyLog(Chunk c, int x, int y, int z, int r)
    {
        for (int dx = -r; dx <= r; dx++)
        for (int dz = -r; dz <= r; dz++)
        {
            int ax = x + dx;
            int az = z + dz;
            if (ax < 0 || ax > 15 || az < 0 || az > 15) continue;
            BlockType t = c.GetBlockLocal(ax, y, az);
            if (t == BlockType.OakLog || t == BlockType.BirchLog || t == BlockType.SpruceLog || t == BlockType.JungleLog || t == BlockType.AcaciaLog) return true;
        }
        return false;
    }

    static void PlaceSpruce(Chunk c, int x, int y, int z, VoxelWorld w)
    {
        int h = 6 + (Mathf.Abs(w.Hash3(x, y, z)) % 4);
        for (int i = 1; i <= h; i++) if (c.GetBlockLocal(x, y + i, z) == BlockType.Air) c.SetBlockLocal(x, y + i, z, BlockType.SpruceLog);

        int r = 0;
        for (int ly = h; ly >= 2; ly--)
        {
            r = (h - ly) / 2 + 1;
            if (r > 3) r = 3;
            if (ly == h) r = 0;
            else if (ly == h - 1) r = 1;

            for (int lx = x - r; lx <= x + r; lx++)
            for (int lz = z - r; lz <= z + r; lz++)
            {
                if (Mathf.Abs(lx - x) == r && Mathf.Abs(lz - z) == r && r > 1) continue;
                SetL(c, lx, y + ly + 1, lz, BlockType.SpruceLeaves);
            }
        }
    }

    static void PlaceAcacia(Chunk c, int x, int y, int z, VoxelWorld w)
    {
        int h = 5 + (Mathf.Abs(w.Hash3(x, y, z)) % 3);
        int dx = (w.Hash3(x, y, z) % 2 == 0) ? 1 : -1;

        for (int i = 1; i <= h; i++)
        {
            int curX = (i > 3) ? x + dx : x;
            if (c.GetBlockLocal(curX, y + i, z) == BlockType.Air) c.SetBlockLocal(curX, y + i, z, BlockType.AcaciaLog);
            if (i == h) PlaceFlatCanopy(c, curX, y + i, z, BlockType.AcaciaLeaves);
        }
    }

    static void PlaceFlatCanopy(Chunk c, int x, int y, int z, BlockType leaf)
    {
        for (int lx = x - 2; lx <= x + 2; lx++)
        for (int lz = z - 2; lz <= z + 2; lz++)
        {
            if (Mathf.Abs(lx - x) == 2 && Mathf.Abs(lz - z) == 2) continue;
            SetL(c, lx, y, lz, leaf);
        }

        for (int lx = x - 1; lx <= x + 1; lx++)
        for (int lz = z - 1; lz <= z + 1; lz++) SetL(c, lx, y + 1, lz, leaf);
    }

    static void PlaceJungle(Chunk c, int x, int y, int z, VoxelWorld w)
    {
        int h = 8 + (Mathf.Abs(w.Hash3(x, y, z)) % 7);
        for (int i = 1; i <= h; i++) if (c.GetBlockLocal(x, y + i, z) == BlockType.Air) c.SetBlockLocal(x, y + i, z, BlockType.JungleLog);
        PlaceLeafBlob(c, x, y + h, z, BlockType.JungleLeaves);
    }

    static void PlaceCactus(Chunk c, int x, int y, int z, VoxelWorld w)
    {
        int h = 2 + (Mathf.Abs(w.Hash3(x, y, z)) % 3);
        for (int i = 1; i <= h; i++)
        {
            if (c.GetBlockLocal(x, y + i, z) != BlockType.Air) break;
            c.SetBlockLocal(x, y + i, z, BlockType.Cactus);
        }
    }

    static void PlaceLeafBlob(Chunk c, int x, int y, int z, BlockType leaf)
    {
        for (int ly = -1; ly <= 1; ly++)
        for (int lx = -2; lx <= 2; lx++)
        for (int lz = -2; lz <= 2; lz++)
        {
            if (Mathf.Abs(lx) + Mathf.Abs(ly) + Mathf.Abs(lz) > 3) continue;
            SetL(c, x + lx, y + ly, z + lz, leaf);
        }
    }

    static void SetL(Chunk c, int x, int y, int z, BlockType t)
    {
        if (c.GetBlockLocal(x, y, z) == BlockType.Air) c.SetBlockLocal(x, y, z, t);
    }
}