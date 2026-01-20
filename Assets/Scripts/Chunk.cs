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

    const int SubmeshCount = 17;

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
            mats.grassTop, mats.grassSide, mats.dirt, mats.stone, mats.sand, mats.water,
            mats.logTop, mats.logSide, mats.leaves, mats.bedrock,
            mats.coalOre, mats.ironOre, mats.redstoneOre, mats.lapisOre,
            mats.diamondOre, mats.emeraldOre, mats.goldOre
        };

        blocks = new BlockType[VoxelData.ChunkSize, VoxelData.ChunkHeight, VoxelData.ChunkSize];
        GenerateBaseTerrain();
        GenerateOres();
        GenerateTrees();
        Rebuild();
    }

    public void SetActive(bool active)
    {
        if (gameObject.activeSelf != active) gameObject.SetActive(active);
    }

    public Vector3Int WorldToLocal(Vector3Int worldPos)
    {
        return new Vector3Int(worldPos.x - coord.x * VoxelData.ChunkSize, worldPos.y, worldPos.z - coord.y * VoxelData.ChunkSize);
    }

    public Vector3Int LocalToWorld(Vector3Int localPos)
    {
        return new Vector3Int(localPos.x + coord.x * VoxelData.ChunkSize, localPos.y, localPos.z + coord.y * VoxelData.ChunkSize);
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
        for (int z = 0; z < VoxelData.ChunkSize; z++)
        {
            int wx = coord.x * VoxelData.ChunkSize + x;
            int wz = coord.y * VoxelData.ChunkSize + z;
            for (int y = 0; y < VoxelData.ChunkHeight; y++)
                blocks[x, y, z] = world.GetGeneratedBlock(wx, y, wz);
        }
    }

    private void GenerateOres()
    {
        System.Random rng = new System.Random(world.Hash3(coord.x, 0, coord.y));
        
        GenerateVein(rng, BlockType.CoalOre, 20, 17, 0, 127);
        GenerateVein(rng, BlockType.IronOre, 20, 9, 0, 63);
        GenerateVein(rng, BlockType.GoldOre, 2, 9, 0, 31);
        GenerateVein(rng, BlockType.RedstoneOre, 8, 8, 0, 15);
        GenerateVein(rng, BlockType.LapisOre, 1, 7, 0, 31);
        GenerateVein(rng, BlockType.DiamondOre, 1, 8, 0, 15);

        if (world.GetBiome(coord.x * VoxelData.ChunkSize, coord.y * VoxelData.ChunkSize) == VoxelWorld.BiomeType.Mountains)
        {
            GenerateVein(rng, BlockType.EmeraldOre, 5, 1, 32, 127);
        }
    }

    private void GenerateVein(System.Random rng, BlockType type, int attempts, int size, int minH, int maxH)
    {
        for (int i = 0; i < attempts; i++)
        {
            int x = rng.Next(0, VoxelData.ChunkSize);
            int z = rng.Next(0, VoxelData.ChunkSize);
            int y = rng.Next(minH, maxH + 1);
            if (y >= VoxelData.ChunkHeight) continue;

            int vx = x, vy = y, vz = z;
            for (int j = 0; j < size; j++)
            {
                if (vx >= 0 && vx < VoxelData.ChunkSize && vz >= 0 && vz < VoxelData.ChunkSize && vy >= 0 && vy < VoxelData.ChunkHeight)
                {
                    if (blocks[vx, vy, vz] == BlockType.Stone)
                        blocks[vx, vy, vz] = type;
                }
                int d = rng.Next(6);
                if (d == 0) vx++; else if (d == 1) vx--; else if (d == 2) vz++; else if (d == 3) vz--; else if (d == 4) vy++; else vy--;
            }
        }
    }

    private void GenerateTrees()
    {
        // Random check to ensure trees are spaced out
        // Use a simple Poisson-disk-like check by hashing
        
        for (int x = 2; x < VoxelData.ChunkSize - 2; x++)
        for (int z = 2; z < VoxelData.ChunkSize - 2; z++)
        {
            int wx = coord.x * VoxelData.ChunkSize + x;
            int wz = coord.y * VoxelData.ChunkSize + z;

            VoxelWorld.BiomeType biome = world.GetBiome(wx, wz);
            
            // Tree probability based on biome
            float chance = 0f;
            if (biome == VoxelWorld.BiomeType.Forest) chance = 0.025f; // reduced from high value
            else if (biome == VoxelWorld.BiomeType.Plains) chance = 0.002f; // very rare
            else continue; // No trees in Desert, Ocean, etc.

            if ((world.Hash3(wx, 100, wz) % 1000) / 1000f > chance) continue;

            int sy = world.GetLandHeight(wx, wz);
            
            // Check limits
            if (sy <= VoxelWorld.SeaLevel + 1 || sy >= VoxelData.ChunkHeight - 10) continue;
            if (blocks[x, sy, z] != BlockType.Grass) continue;

            // Simple check to prevent overlapping trunks in the same chunk
            bool spaceClear = true;
            for(int nx = x-2; nx <= x+2; nx++)
            for(int nz = z-2; nz <= z+2; nz++)
            {
                if (nx==x && nz==z) continue;
                if (blocks[nx, sy+1, nz] == BlockType.Log) spaceClear = false;
            }
            if (!spaceClear) continue;

            // Generate Tree
            int h = 4 + (world.Hash3(wx, sy + 1, wz) % 3);
            for (int y = 1; y <= h; y++) blocks[x, sy + y, z] = BlockType.Log;
            for (int lx = -2; lx <= 2; lx++)
            for (int lz = -2; lz <= 2; lz++)
            for (int ly = h - 2; ly <= h + 1; ly++)
            {
                if (Mathf.Abs(lx) == 2 && Mathf.Abs(lz) == 2 && ly >= h) continue;
                if (ly > h && (Mathf.Abs(lx) == 2 || Mathf.Abs(lz) == 2)) continue;
                
                int ax = x + lx;
                int az = z + lz;
                int ay = sy + ly;
                if (ax >= 0 && ax < VoxelData.ChunkSize && az >= 0 && az < VoxelData.ChunkSize && ay < VoxelData.ChunkHeight)
                {
                    if (blocks[ax, ay, az] == BlockType.Air) blocks[ax, ay, az] = BlockType.Leaves;
                }
            }
        }
    }

    public void Rebuild()
    {
        List<Vector3> verts = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<Color> cols = new List<Color>();
        List<int>[] tris = new List<int>[SubmeshCount];
        for (int i = 0; i < SubmeshCount; i++) tris[i] = new List<int>();
        List<Vector3> cVerts = new List<Vector3>();
        List<int> cTris = new List<int>();
        int vIdx = 0;
        int cVIdx = 0;

        for (int x = 0; x < VoxelData.ChunkSize; x++)
        for (int y = 0; y < VoxelData.ChunkHeight; y++)
        for (int z = 0; z < VoxelData.ChunkSize; z++)
        {
            BlockType bt = blocks[x, y, z];
            if (bt == BlockType.Air) continue;
            int wx = coord.x * VoxelData.ChunkSize + x;
            int wz = coord.y * VoxelData.ChunkSize + z;
            world.GetBiomeTintsAt(wx, wz, out Color grassTint, out Color foliageTint);

            for (int f = 0; f < 6; f++)
            {
                Vector3Int n = new Vector3Int(x, y, z) + Vector3Int.RoundToInt(VoxelData.FaceChecks[f]);
                BlockType nb = BlockType.Air;
                if (n.x >= 0 && n.x < VoxelData.ChunkSize && n.z >= 0 && n.z < VoxelData.ChunkSize && n.y >= 0 && n.y < VoxelData.ChunkHeight)
                    nb = blocks[n.x, n.y, n.z];
                else
                    nb = world.GetBlock(LocalToWorld(n));

                bool render = false;
                if (bt == BlockType.Water) render = (nb == BlockType.Air);
                else render = (nb == BlockType.Air || nb == BlockType.Water);

                if (render)
                {
                    int sub = 3;
                    if (bt == BlockType.Grass) { if (f==2) sub=0; else if (f==3) sub=2; else sub=1; }
                    else if (bt == BlockType.Dirt) sub=2;
                    else if (bt == BlockType.Sand) sub=4;
                    else if (bt == BlockType.Water) sub=5;
                    else if (bt == BlockType.Log) { if (f==2 || f==3) sub=6; else sub=7; }
                    else if (bt == BlockType.Leaves) sub=8;
                    else if (bt == BlockType.Bedrock) sub=9;
                    else if (bt == BlockType.CoalOre) sub=10;
                    else if (bt == BlockType.IronOre) sub=11;
                    else if (bt == BlockType.RedstoneOre) sub=12;
                    else if (bt == BlockType.LapisOre) sub=13;
                    else if (bt == BlockType.DiamondOre) sub=14;
                    else if (bt == BlockType.EmeraldOre) sub=15;
                    else if (bt == BlockType.GoldOre) sub=16;

                    Color c = Color.white;
                    if (sub == 0 || sub == 1) c = grassTint;
                    if (sub == 8) c = foliageTint;

                    for (int i = 0; i < 4; i++)
                    {
                        verts.Add(new Vector3(x,y,z) + VoxelData.Verts[VoxelData.Tris[f, i]]);
                        uvs.Add(VoxelData.BaseUVs[i]);
                        cols.Add(c);
                    }
                    tris[sub].Add(vIdx); tris[sub].Add(vIdx+1); tris[sub].Add(vIdx+2);
                    tris[sub].Add(vIdx); tris[sub].Add(vIdx+2); tris[sub].Add(vIdx+3);
                    vIdx += 4;

                    if (bt != BlockType.Water)
                    {
                        cVerts.Add(verts[verts.Count-4]); cVerts.Add(verts[verts.Count-3]);
                        cVerts.Add(verts[verts.Count-2]); cVerts.Add(verts[verts.Count-1]);
                        cTris.Add(cVIdx); cTris.Add(cVIdx+1); cTris.Add(cVIdx+2);
                        cTris.Add(cVIdx); cTris.Add(cVIdx+2); cTris.Add(cVIdx+3);
                        cVIdx += 4;
                    }
                }
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
        meshFilter.sharedMesh = m;

        Mesh cm = new Mesh();
        cm.indexFormat = (cVerts.Count > 65000) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        cm.SetVertices(cVerts);
        cm.SetTriangles(cTris, 0);
        cm.RecalculateNormals();
        meshCollider.sharedMesh = cm;
    }
}