using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class Chunk : MonoBehaviour
{
    private VoxelWorld world;
    private Vector2Int coord;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;

    private BlockType[,,] blocks;
    private VoxelWorld.BlockMaterials mats;

    const int SM_GrassTop = 0;
    const int SM_GrassSide = 1;
    const int SM_Dirt = 2;
    const int SM_Stone = 3;
    const int SM_Sand = 4;
    const int SM_Water = 5;
    const int SM_LogTop = 6;
    const int SM_LogSide = 7;
    const int SM_Leaves = 8;
    const int SM_Bedrock = 9;
    const int SM_Coal = 10;
    const int SM_Iron = 11;
    const int SM_Redstone = 12;
    const int SM_Lapis = 13;
    const int SM_Diamond = 14;
    const int SM_Emerald = 15;

    const int SubmeshCount = 16;

    const int TreeEdgePadding = 2;

    public void Init(VoxelWorld world, Vector2Int coord, VoxelWorld.BlockMaterials materials)
    {
        this.world = world;
        this.coord = coord;
        mats = materials;

        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();

        if (world != null && world.terrainColliderMaterial != null)
            meshCollider.sharedMaterial = world.terrainColliderMaterial;

        meshRenderer.sharedMaterials = new Material[]
        {
            mats.grassTop,
            mats.grassSide,
            mats.dirt,
            mats.stone,
            mats.sand,
            mats.water,
            mats.logTop,
            mats.logSide,
            mats.leaves,
            mats.bedrock,
            mats.coalOre,
            mats.ironOre,
            mats.redstoneOre,
            mats.lapisOre,
            mats.diamondOre,
            mats.emeraldOre
        };

        blocks = new BlockType[VoxelData.ChunkSize, VoxelData.ChunkHeight, VoxelData.ChunkSize];

        GenerateBaseTerrain();
        GenerateOresMinecraftLike();
        GenerateTreesBiomePatches();

        Rebuild();
    }

    public void SetActive(bool active)
    {
        if (gameObject.activeSelf != active)
            gameObject.SetActive(active);
    }

    public Vector3Int WorldToLocal(Vector3Int worldPos)
    {
        int lx = worldPos.x - coord.x * VoxelData.ChunkSize;
        int lz = worldPos.z - coord.y * VoxelData.ChunkSize;
        return new Vector3Int(lx, worldPos.y, lz);
    }

    public Vector3Int LocalToWorld(Vector3Int localPos)
    {
        int wx = localPos.x + coord.x * VoxelData.ChunkSize;
        int wz = localPos.z + coord.y * VoxelData.ChunkSize;
        return new Vector3Int(wx, localPos.y, wz);
    }

    public BlockType GetBlockLocal(Vector3Int localPos)
    {
        if (localPos.x < 0 || localPos.x >= VoxelData.ChunkSize) return BlockType.Air;
        if (localPos.z < 0 || localPos.z >= VoxelData.ChunkSize) return BlockType.Air;
        if (localPos.y < 0 || localPos.y >= VoxelData.ChunkHeight) return BlockType.Air;
        return blocks[localPos.x, localPos.y, localPos.z];
    }

    public void SetBlockLocal(Vector3Int localPos, BlockType type)
    {
        if (localPos.x < 0 || localPos.x >= VoxelData.ChunkSize) return;
        if (localPos.z < 0 || localPos.z >= VoxelData.ChunkSize) return;
        if (localPos.y < 0 || localPos.y >= VoxelData.ChunkHeight) return;
        blocks[localPos.x, localPos.y, localPos.z] = type;
    }

    private void GenerateBaseTerrain()
    {
        for (int x = 0; x < VoxelData.ChunkSize; x++)
        {
            for (int z = 0; z < VoxelData.ChunkSize; z++)
            {
                int wx = coord.x * VoxelData.ChunkSize + x;
                int wz = coord.y * VoxelData.ChunkSize + z;

                for (int y = 0; y < VoxelData.ChunkHeight; y++)
                    blocks[x, y, z] = world.GetGeneratedBlock(wx, y, wz);
            }
        }
    }

    private void GenerateOresMinecraftLike()
    {
        world.GetOreSettings(out var o);

        int chunkSeed = world.Hash3(coord.x, 0, coord.y);
        System.Random rng = new System.Random(chunkSeed);

        PlaceVeins(rng, BlockType.CoalOre, o.coal);
        PlaceVeins(rng, BlockType.IronOre, o.iron);
        PlaceVeins(rng, BlockType.LapisOre, o.lapis, triangularY: true);
        PlaceVeins(rng, BlockType.RedstoneOre, o.redstone);
        PlaceVeins(rng, BlockType.DiamondOre, o.diamond);

        int avgSurface = EstimateChunkAverageSurface();
        if (avgSurface >= VoxelWorld.SeaLevel + 22)
            PlaceVeins(rng, BlockType.EmeraldOre, o.emerald, emeraldScatter: true);
    }

    private int EstimateChunkAverageSurface()
    {
        int sum = 0;
        int cnt = 0;
        for (int x = 0; x < VoxelData.ChunkSize; x += 4)
        for (int z = 0; z < VoxelData.ChunkSize; z += 4)
        {
            int wx = coord.x * VoxelData.ChunkSize + x;
            int wz = coord.y * VoxelData.ChunkSize + z;
            sum += world.GetLandHeight(wx, wz);
            cnt++;
        }
        return (cnt == 0) ? VoxelWorld.SeaLevel : (sum / cnt);
    }

    private void PlaceVeins(System.Random rng, BlockType oreType, VoxelWorld.OreLayer layer, bool triangularY = false, bool emeraldScatter = false)
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
                if (blocks[x, y, z] == BlockType.Stone)
                    blocks[x, y, z] = oreType;
                continue;
            }

            int veinSize = Mathf.Max(1, layer.veinSize);
            int vx = x, vy = y, vz = z;

            for (int v = 0; v < veinSize; v++)
            {
                if (vx < 1 || vx >= VoxelData.ChunkSize - 1) break;
                if (vz < 1 || vz >= VoxelData.ChunkSize - 1) break;
                if (vy < 1 || vy >= VoxelData.ChunkHeight - 1) break;

                if (blocks[vx, vy, vz] == BlockType.Stone)
                    blocks[vx, vy, vz] = oreType;

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

    private void GenerateTreesBiomePatches()
    {
        for (int x = TreeEdgePadding; x < VoxelData.ChunkSize - TreeEdgePadding; x++)
        {
            for (int z = TreeEdgePadding; z < VoxelData.ChunkSize - TreeEdgePadding; z++)
            {
                int wx = coord.x * VoxelData.ChunkSize + x;
                int wz = coord.y * VoxelData.ChunkSize + z;

                world.GetBiomeParams(wx, wz, out float temp, out float rain, out float forest, out float ocean);

                if (ocean >= world.oceanThreshold) continue;

                int surfaceY = world.GetLandHeight(wx, wz);
                if (surfaceY <= VoxelWorld.SeaLevel + 1) continue;
                if (surfaceY < 1 || surfaceY >= VoxelData.ChunkHeight - 10) continue;
                if (blocks[x, surfaceY, z] != BlockType.Grass) continue;

                bool isForest = forest > 0.58f;

                float humidityFactor = Mathf.Lerp(0.55f, 1.0f, rain);
                float chance = isForest ? (0.030f * humidityFactor) : (0.006f * humidityFactor);

                int h = world.Hash3(wx, surfaceY, wz);
                float r01 = (Mathf.Abs(h) % 10000) / 10000f;
                if (r01 > chance) continue;

                float bigChance = isForest ? 0.10f : 0.03f;
                float r02 = (Mathf.Abs(world.Hash3(wx + 17, surfaceY + 3, wz - 9)) % 10000) / 10000f;
                bool big = r02 < bigChance;

                if (big)
                    TryPlaceBigOak(x, surfaceY, z, wx, wz);
                else
                    TryPlaceSmallOak(x, surfaceY, z, wx, wz);
            }
        }
    }

    private bool TryPlaceSmallOak(int x, int surfaceY, int z, int wx, int wz)
    {
        int trunkH = 4 + (Mathf.Abs(world.Hash3(wx, surfaceY, wz)) % 3);

        int trunkBelowLeaves = 2 + (Mathf.Abs(world.Hash3(wx + 5, surfaceY, wz + 5)) % 2);
        int leafBaseY = surfaceY + trunkBelowLeaves;
        int topY = surfaceY + trunkH;

        if (topY + 3 >= VoxelData.ChunkHeight) return false;

        for (int y = surfaceY + 1; y <= topY; y++)
            if (blocks[x, y, z] != BlockType.Air) return false;

        for (int y = surfaceY + 1; y <= topY; y++)
            blocks[x, y, z] = BlockType.Log;

        PlaceLeavesSquare(x, z, leafBaseY + 1, 2, false);
        PlaceLeavesSquare(x, z, leafBaseY + 2, 2, true);
        PlaceLeavesSquare(x, z, leafBaseY + 3, 1, true);
        PlaceLeavesCross(x, z, leafBaseY + 4);

        return true;
    }

    private bool TryPlaceBigOak(int x, int surfaceY, int z, int wx, int wz)
    {
        int trunkH = 8 + (Mathf.Abs(world.Hash3(wx, surfaceY, wz)) % 5);
        int trunkBelowLeaves = 3;
        int leafBaseY = surfaceY + trunkBelowLeaves;
        int topY = surfaceY + trunkH;

        if (topY + 4 >= VoxelData.ChunkHeight) return false;

        for (int y = surfaceY + 1; y <= topY; y++)
            if (blocks[x, y, z] != BlockType.Air) return false;

        for (int y = surfaceY + 1; y <= topY; y++)
            blocks[x, y, z] = BlockType.Log;

        PlaceLeavesSquare(x, z, leafBaseY + 1, 3, false);
        PlaceLeavesSquare(x, z, leafBaseY + 2, 3, true);
        PlaceLeavesSquare(x, z, leafBaseY + 3, 2, true);
        PlaceLeavesSquare(x, z, leafBaseY + 4, 2, false);
        PlaceLeavesCross(x, z, leafBaseY + 5);

        int branches = 2 + (Mathf.Abs(world.Hash3(wx + 31, surfaceY, wz - 7)) % 2);
        for (int i = 0; i < branches; i++)
        {
            int dir = (Mathf.Abs(world.Hash3(wx + i * 19, surfaceY + i, wz + i * 13)) % 4);
            int dx = (dir == 0) ? 1 : (dir == 1) ? -1 : 0;
            int dz = (dir == 2) ? 1 : (dir == 3) ? -1 : 0;

            int by = leafBaseY + 2 + i;
            int len = 2 + (Mathf.Abs(world.Hash3(wx + 77, by, wz + 77)) % 2);

            int ax = x, az = z;
            for (int s = 0; s < len; s++)
            {
                ax += dx; az += dz;
                if (ax < 1 || ax >= VoxelData.ChunkSize - 1) break;
                if (az < 1 || az >= VoxelData.ChunkSize - 1) break;
                if (by < 1 || by >= VoxelData.ChunkHeight - 1) break;

                if (blocks[ax, by, az] == BlockType.Air)
                    blocks[ax, by, az] = BlockType.Log;

                PlaceLeavesSquare(ax, az, by + 1, 1, true);
                PlaceLeavesCross(ax, az, by + 2);
            }
        }

        return true;
    }

    private void PlaceLeavesSquare(int cx, int cz, int y, int r, bool allowCorners)
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

            if (blocks[ax, y, az] == BlockType.Air)
                blocks[ax, y, az] = BlockType.Leaves;
        }
    }

    private void PlaceLeavesCross(int cx, int cz, int y)
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

            if (blocks[ax, y, az] == BlockType.Air)
                blocks[ax, y, az] = BlockType.Leaves;
        }
    }

    public void Rebuild()
    {
        List<Vector3> verts = new List<Vector3>(8192);
        List<Vector2> uvs = new List<Vector2>(8192);
        List<Color> cols = new List<Color>(8192);

        List<int>[] tris = new List<int>[SubmeshCount];
        for (int i = 0; i < SubmeshCount; i++) tris[i] = new List<int>(8192);

        List<Vector3> cVerts = new List<Vector3>(8192);
        List<int> cTris = new List<int>(8192);

        int vertIndex = 0;
        int cVertIndex = 0;

        for (int x = 0; x < VoxelData.ChunkSize; x++)
        for (int y = 0; y < VoxelData.ChunkHeight; y++)
        for (int z = 0; z < VoxelData.ChunkSize; z++)
        {
            BlockType bt = blocks[x, y, z];
            if (bt == BlockType.Air) continue;

            int wx = coord.x * VoxelData.ChunkSize + x;
            int wz = coord.y * VoxelData.ChunkSize + z;

            world.GetBiomeTintsAt(wx, wz, out Color grassTint, out Color foliageTint);

            Vector3Int local = new Vector3Int(x, y, z);

            for (int face = 0; face < 6; face++)
            {
                Vector3Int neighbor = local + Vector3Int.RoundToInt(VoxelData.FaceChecks[face]);
                BlockType nb = GetNeighborBlock(neighbor);

                if (bt == BlockType.Water)
                {
                    if (nb != BlockType.Air) continue;
                }
                else
                {
                    if (nb != BlockType.Air && nb != BlockType.Water) continue;
                }

                int sub = PickSubmesh(bt, face);

                Color vc = Color.white;
                if (sub == SM_GrassTop || sub == SM_GrassSide) vc = grassTint;
                else if (sub == SM_Leaves) vc = foliageTint;

                for (int i = 0; i < 4; i++)
                {
                    int v = VoxelData.Tris[face, i];
                    verts.Add(new Vector3(x, y, z) + VoxelData.Verts[v]);
                    uvs.Add(VoxelData.BaseUVs[i]);
                    cols.Add(vc);
                }

                tris[sub].Add(vertIndex + 0);
                tris[sub].Add(vertIndex + 1);
                tris[sub].Add(vertIndex + 2);
                tris[sub].Add(vertIndex + 0);
                tris[sub].Add(vertIndex + 2);
                tris[sub].Add(vertIndex + 3);

                if (bt != BlockType.Water)
                {
                    int baseI = cVertIndex;
                    cVerts.Add(verts[verts.Count - 4]);
                    cVerts.Add(verts[verts.Count - 3]);
                    cVerts.Add(verts[verts.Count - 2]);
                    cVerts.Add(verts[verts.Count - 1]);
                    cTris.Add(baseI + 0);
                    cTris.Add(baseI + 1);
                    cTris.Add(baseI + 2);
                    cTris.Add(baseI + 0);
                    cTris.Add(baseI + 2);
                    cTris.Add(baseI + 3);
                    cVertIndex += 4;
                }

                vertIndex += 4;
            }
        }

        Mesh m = new Mesh();
        m.indexFormat = (verts.Count > 65000) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        m.SetVertices(verts);
        m.SetUVs(0, uvs);
        m.SetColors(cols);
        m.subMeshCount = SubmeshCount;
        for (int i = 0; i < SubmeshCount; i++) m.SetTriangles(tris[i], i);
        m.RecalculateNormals();
        m.RecalculateBounds();
        meshFilter.sharedMesh = m;

        Mesh cm = new Mesh();
        cm.indexFormat = (cVerts.Count > 65000) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        cm.SetVertices(cVerts);
        cm.SetTriangles(cTris, 0);
        cm.RecalculateNormals();
        cm.RecalculateBounds();
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = cm;
    }

    private int PickSubmesh(BlockType bt, int face)
    {
        switch (bt)
        {
            case BlockType.Water: return SM_Water;
            case BlockType.Sand: return SM_Sand;
            case BlockType.Stone: return SM_Stone;
            case BlockType.Bedrock: return SM_Bedrock;

            case BlockType.CoalOre: return SM_Coal;
            case BlockType.IronOre: return SM_Iron;
            case BlockType.RedstoneOre: return SM_Redstone;
            case BlockType.LapisOre: return SM_Lapis;
            case BlockType.DiamondOre: return SM_Diamond;
            case BlockType.EmeraldOre: return SM_Emerald;

            case BlockType.Leaves: return SM_Leaves;
            case BlockType.Dirt: return SM_Dirt;

            case BlockType.Grass:
                if (face == 2) return SM_GrassTop;
                if (face == 3) return SM_Dirt;
                return SM_GrassSide;

            case BlockType.Log:
                if (face == 2 || face == 3) return SM_LogTop;
                return SM_LogSide;

            default:
                return SM_Stone;
        }
    }

    private BlockType GetNeighborBlock(Vector3Int local)
    {
        if (local.y < 0 || local.y >= VoxelData.ChunkHeight) return BlockType.Air;

        if (local.x >= 0 && local.x < VoxelData.ChunkSize &&
            local.z >= 0 && local.z < VoxelData.ChunkSize)
        {
            return blocks[local.x, local.y, local.z];
        }

        Vector3Int wp = LocalToWorld(local);
        return world.GetBlock(wp);
    }
}
