using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class Chunk : MonoBehaviour
{
    public VoxelWorld World => world;
    public Vector2Int Coord => coord;

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
    const int SM_Gold = 16;

    const int SubmeshCount = 17;

    // GC/alloc csökkentés
    private readonly List<Vector3> verts = new List<Vector3>(8192);
    private readonly List<Vector2> uvs = new List<Vector2>(8192);
    private readonly List<Color> cols = new List<Color>(8192);
    private readonly List<int>[] tris = new List<int>[SubmeshCount];

    private readonly List<Vector3> cVerts = new List<Vector3>(8192);
    private readonly List<int> cTris = new List<int>(8192);

    private Mesh mesh;
    private Mesh colliderMesh;

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
            mats.emeraldOre,
            mats.goldOre
        };

        blocks = new BlockType[VoxelData.ChunkSize, VoxelData.ChunkHeight, VoxelData.ChunkSize];

        for (int i = 0; i < SubmeshCount; i++)
            tris[i] = new List<int>(8192);

        GenerateBaseTerrain();

        // feature külön fájlban
        ChunkFeatureGenerator.Generate(this);

        mesh = new Mesh();
        colliderMesh = new Mesh();

        Rebuild();
    }

    public void SetActive(bool active)
    {
        if (gameObject.activeSelf != active)
            gameObject.SetActive(active);
    }

    public void SetColliderEnabled(bool enabled)
    {
        if (meshCollider != null && meshCollider.enabled != enabled)
            meshCollider.enabled = enabled;
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

    // Feature gen API
    public BlockType GetBlockLocal(int x, int y, int z)
    {
        if (x < 0 || x >= VoxelData.ChunkSize) return BlockType.Air;
        if (z < 0 || z >= VoxelData.ChunkSize) return BlockType.Air;
        if (y < 0 || y >= VoxelData.ChunkHeight) return BlockType.Air;
        return blocks[x, y, z];
    }

    public void SetBlockLocal(int x, int y, int z, BlockType type)
    {
        if (x < 0 || x >= VoxelData.ChunkSize) return;
        if (z < 0 || z >= VoxelData.ChunkSize) return;
        if (y < 0 || y >= VoxelData.ChunkHeight) return;
        blocks[x, y, z] = type;
    }

    public BlockType GetBlockLocal(Vector3Int localPos) => GetBlockLocal(localPos.x, localPos.y, localPos.z);
    public void SetBlockLocal(Vector3Int localPos, BlockType type) => SetBlockLocal(localPos.x, localPos.y, localPos.z, type);

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

    public void Rebuild()
    {
        verts.Clear();
        uvs.Clear();
        cols.Clear();
        cVerts.Clear();
        cTris.Clear();
        for (int i = 0; i < SubmeshCount; i++) tris[i].Clear();

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

                // VÍZ + SZILÁRD találkozás:
                // - Szilárd: rajzolj air vagy water felé (part látszódik)
                // - Water: csak air felé (így nincs z-fighting a parton)
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

        mesh.Clear();
        mesh.indexFormat = (verts.Count > 65000)
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(cols);
        mesh.subMeshCount = SubmeshCount;
        for (int i = 0; i < SubmeshCount; i++) mesh.SetTriangles(tris[i], i);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        meshFilter.sharedMesh = mesh;

        colliderMesh.Clear();
        colliderMesh.indexFormat = (cVerts.Count > 65000)
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        colliderMesh.SetVertices(cVerts);
        colliderMesh.SetTriangles(cTris, 0);
        colliderMesh.RecalculateNormals();
        colliderMesh.RecalculateBounds();
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = colliderMesh;
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
            case BlockType.GoldOre: return SM_Gold;
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
