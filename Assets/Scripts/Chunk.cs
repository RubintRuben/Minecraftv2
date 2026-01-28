using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class Chunk : MonoBehaviour
{
    public VoxelWorld World => world;
    public Vector2Int Coord => coord;

    VoxelWorld world;
    Vector2Int coord;

    MeshFilter meshFilter;
    MeshRenderer meshRenderer;
    MeshCollider meshCollider;

    BlockType[,,] blocks;
    VoxelWorld.BlockMaterials mats;

    const int SubmeshCount = 35;

    readonly List<Vector3> verts = new List<Vector3>(4096);
    readonly List<Vector2> uvs = new List<Vector2>(4096);
    readonly List<Color> cols = new List<Color>(4096);
    readonly List<int>[] tris = new List<int>[SubmeshCount];

    readonly List<Vector3> cVerts = new List<Vector3>(4096);
    readonly List<int> cTris = new List<int>(4096);

    Mesh mesh;
    Mesh colliderMesh;

    static readonly BlockType[] colBuf = new BlockType[VoxelData.ChunkHeight];

    const int SM_GrassTop = 0; const int SM_GrassSide = 1; const int SM_Dirt = 2; const int SM_Stone = 3;
    const int SM_Sand = 4; const int SM_Water = 5; const int SM_Bedrock = 6; const int SM_Gravel = 7; const int SM_Clay = 8;
    const int SM_Coal = 9; const int SM_Iron = 10; const int SM_Gold = 11; const int SM_Redstone = 12;
    const int SM_Lapis = 13; const int SM_Diamond = 14; const int SM_Emerald = 15;
    const int SM_OakLogTop = 16; const int SM_OakLogSide = 17; const int SM_OakLeaves = 18;
    const int SM_BirchLogTop = 19; const int SM_BirchLogSide = 20; const int SM_BirchLeaves = 21;
    const int SM_SpruceLogTop = 22; const int SM_SpruceLogSide = 23; const int SM_SpruceLeaves = 24;
    const int SM_JungleLogTop = 25; const int SM_JungleLogSide = 26; const int SM_JungleLeaves = 27;
    const int SM_AcaciaLogTop = 28; const int SM_AcaciaLogSide = 29; const int SM_AcaciaLeaves = 30;
    const int SM_CactusTop = 31; const int SM_CactusSide = 32; const int SM_CactusBottom = 33; const int SM_DeadBush = 34;

    public void Init(VoxelWorld world, Vector2Int coord, VoxelWorld.BlockMaterials materials)
    {
        this.world = world;
        this.coord = coord;
        mats = materials;

        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();

        meshRenderer.sharedMaterials = new Material[]
        {
            mats.grassTop, mats.grassSide, mats.dirt, mats.stone, mats.sand, mats.water, mats.bedrock,
            mats.gravel, mats.clay,
            mats.coalOre, mats.ironOre, mats.goldOre, mats.redstoneOre, mats.lapisOre, mats.diamondOre, mats.emeraldOre,
            mats.oakLogTop, mats.oakLogSide, mats.oakLeaves,
            mats.birchLogTop, mats.birchLogSide, mats.birchLeaves,
            mats.spruceLogTop, mats.spruceLogSide, mats.spruceLeaves,
            mats.jungleLogTop, mats.jungleLogSide, mats.jungleLeaves,
            mats.acaciaLogTop, mats.acaciaLogSide, mats.acaciaLeaves,
            mats.cactusTop, mats.cactusSide, mats.cactusBottom, mats.deadBush
        };

        blocks = new BlockType[VoxelData.ChunkSize, VoxelData.ChunkHeight, VoxelData.ChunkSize];
        for (int i = 0; i < SubmeshCount; i++) tris[i] = new List<int>(1024);

        GenerateBaseTerrain();
        ChunkFeatureGenerator.Generate(this);

        mesh = new Mesh();
        mesh.MarkDynamic();
        colliderMesh = new Mesh();
        Rebuild();
    }

    public void SetActive(bool active) => gameObject.SetActive(active);
    public Vector3Int WorldToLocal(Vector3Int worldPos) => new Vector3Int(worldPos.x - coord.x * 16, worldPos.y, worldPos.z - coord.y * 16);
    public Vector3Int LocalToWorld(Vector3Int localPos) => new Vector3Int(localPos.x + coord.x * 16, localPos.y, localPos.z + coord.y * 16);

    public BlockType GetBlockLocal(int x, int y, int z)
    {
        if (x < 0 || x >= 16 || z < 0 || z >= 16 || y < 0 || y >= 128) return BlockType.Air;
        return blocks[x, y, z];
    }

    public void SetBlockLocal(int x, int y, int z, BlockType type)
    {
        if (x < 0 || x >= 16 || z < 0 || z >= 16 || y < 0 || y >= 128) return;
        blocks[x, y, z] = type;
    }

    public void SetBlockLocal(Vector3Int p, BlockType type) => SetBlockLocal(p.x, p.y, p.z, type);

    void GenerateBaseTerrain()
    {
        int baseX = coord.x * 16;
        int baseZ = coord.y * 16;

        for (int x = 0; x < 16; x++)
        for (int z = 0; z < 16; z++)
        {
            int wx = baseX + x;
            int wz = baseZ + z;
            world.GenerateColumn(wx, wz, colBuf);
            for (int y = 0; y < 128; y++) blocks[x, y, z] = colBuf[y];
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

        int baseX = coord.x * 16;
        int baseZ = coord.y * 16;
        int vertIndex = 0;
        int cVertIndex = 0;

        BuildGreedyOpaque(ref vertIndex, ref cVertIndex);

        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 128; y++)
        for (int z = 0; z < 16; z++)
        {
            BlockType bt = blocks[x, y, z];
            if (bt == BlockType.Air) continue;
            if (IsGreedy(bt)) continue;

            world.GetBiomeTintsAt(baseX + x, baseZ + z, out Color grassTint, out Color foliageTint);

            if (bt == BlockType.DeadBush)
            {
                AddCrossMesh(x, y, z, SM_DeadBush, ref vertIndex, Color.white);
                continue;
            }

            for (int face = 0; face < 6; face++)
            {
                Vector3Int nbPos = new Vector3Int(x, y, z) + Vector3Int.RoundToInt(VoxelData.FaceChecks[face]);
                BlockType nb = GetNeighborBlock(nbPos);

                if (bt == BlockType.Water) { if (nb != BlockType.Air) continue; }
                else if (nb != BlockType.Air && nb != BlockType.Water && !IsTransparent(nb)) continue;

                int sub = PickSubmesh(bt, face);
                Color vc = Color.white;
                if (sub == SM_GrassTop || sub == SM_GrassSide) vc = grassTint;
                if (IsLeaf(bt)) vc = foliageTint;
                if (sub == SM_Water) vc = new Color(1f, 1f, 1f, 0.85f);

                for (int i = 0; i < 4; i++)
                {
                    Vector3 v = VoxelData.Verts[VoxelData.Tris[face, i]];
                    if (bt == BlockType.Water && v.y > 0.5f) v.y = 0.88f;

                    if (bt == BlockType.Cactus && face >= 2)
                    {
                        if (v.x < 0.1f) v.x = 0.0625f;
                        if (v.x > 0.9f) v.x = 0.9375f;
                        if (v.z < 0.1f) v.z = 0.0625f;
                        if (v.z > 0.9f) v.z = 0.9375f;
                    }

                    verts.Add(new Vector3(x, y, z) + v);
                    uvs.Add(VoxelData.BaseUVs[i]);
                    cols.Add(vc);
                }

                tris[sub].Add(vertIndex);
                tris[sub].Add(vertIndex + 1);
                tris[sub].Add(vertIndex + 2);
                tris[sub].Add(vertIndex);
                tris[sub].Add(vertIndex + 2);
                tris[sub].Add(vertIndex + 3);

                if (bt != BlockType.Water)
                {
                    for (int j = 4; j > 0; j--) cVerts.Add(verts[verts.Count - j]);
                    cTris.Add(cVertIndex);
                    cTris.Add(cVertIndex + 1);
                    cTris.Add(cVertIndex + 2);
                    cTris.Add(cVertIndex);
                    cTris.Add(cVertIndex + 2);
                    cTris.Add(cVertIndex + 3);
                    cVertIndex += 4;
                }

                vertIndex += 4;
            }
        }

        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(cols);
        mesh.subMeshCount = SubmeshCount;
        for (int i = 0; i < SubmeshCount; i++) mesh.SetTriangles(tris[i], i);
        mesh.RecalculateNormals();
        meshFilter.sharedMesh = mesh;

        colliderMesh.Clear();
        colliderMesh.SetVertices(cVerts);
        colliderMesh.SetTriangles(cTris, 0);
        colliderMesh.RecalculateNormals();
        meshCollider.sharedMesh = colliderMesh;
        if (world != null && world.terrainColliderMaterial != null) meshCollider.sharedMaterial = world.terrainColliderMaterial;
    }

    void BuildGreedyOpaque(ref int vertIndex, ref int cVertIndex)
    {
        int sizeX = 16, sizeY = 128, sizeZ = 16;
        int[] dims = { sizeX, sizeY, sizeZ };
        int[] x = new int[3];
        int[] q = new int[3];
        MaskCell[] mask = new MaskCell[Mathf.Max(sizeX * sizeY, Mathf.Max(sizeY * sizeZ, sizeX * sizeZ))];

        for (int d = 0; d < 3; d++)
        {
            int u = (d + 1) % 3;
            int v = (d + 2) % 3;
            q[0] = q[1] = q[2] = 0;
            q[d] = 1;
            int du = dims[u], dv = dims[v];

            for (x[d] = -1; x[d] < dims[d]; )
            {
                int n = 0;
                for (x[v] = 0; x[v] < dv; x[v]++)
                for (x[u] = 0; x[u] < du; x[u]++)
                {
                    BlockType a = GetCellGreedy(x[0], x[1], x[2]);
                    BlockType b = GetCellGreedy(x[0] + q[0], x[1] + q[1], x[2] + q[2]);
                    bool aSolid = a != BlockType.Air && a != BlockType.Water && IsGreedy(a);
                    bool bSolid = b != BlockType.Air && b != BlockType.Water && IsGreedy(b);

                    if (aSolid == bSolid) mask[n++] = default;
                    else if (aSolid) mask[n++] = new MaskCell(a, false);
                    else mask[n++] = new MaskCell(b, true);
                }

                x[d]++;
                n = 0;
                for (int j = 0; j < dv; j++)
                {
                    for (int i = 0; i < du; )
                    {
                        MaskCell m = mask[n];
                        if (!m.valid) { i++; n++; continue; }

                        int w = 1;
                        while (i + w < du && mask[n + w].Equals(m)) w++;

                        int h = 1;
                        bool done = false;
                        while (j + h < dv && !done)
                        {
                            int nn = n + h * du;
                            for (int k = 0; k < w; k++)
                                if (!mask[nn + k].Equals(m)) { done = true; break; }
                            if (!done) h++;
                        }

                        int[] duVec = { 0, 0, 0 }; duVec[u] = w;
                        int[] dvVec = { 0, 0, 0 }; dvVec[v] = h;
                        int[] p = { x[0], x[1], x[2] }; p[u] = i; p[v] = j;

                        AddGreedyQuad(d, u, v, p, duVec, dvVec, m, ref vertIndex, ref cVertIndex);

                        for (int hh = 0; hh < h; hh++)
                        {
                            int nn = n + hh * du;
                            for (int kk = 0; kk < w; kk++) mask[nn + kk] = default;
                        }
                        i += w; n += w;
                    }
                }
            }
        }
    }

    BlockType GetCellGreedy(int x, int y, int z)
    {
        if (y < 0 || y >= 128) return BlockType.Air;
        if (x < 0 || x >= 16 || z < 0 || z >= 16)
        {
            Vector3Int wp = LocalToWorld(new Vector3Int(x, y, z));
            return world.GetBlock(wp);
        }
        return blocks[x, y, z];
    }

    void AddGreedyQuad(int d, int u, int v, int[] p, int[] du, int[] dv, MaskCell m, ref int vertIndex, ref int cVertIndex)
    {
        Vector3 p0 = new Vector3(p[0], p[1], p[2]);
        Vector3 p1 = p0 + new Vector3(du[0], du[1], du[2]);
        Vector3 p2 = p0 + new Vector3(dv[0], dv[1], dv[2]);
        Vector3 p3 = p0 + new Vector3(du[0] + dv[0], du[1] + dv[1], du[2] + dv[2]);

        if (m.backFace) { Vector3 tmp = p1; p1 = p2; p2 = tmp; }

        int face = FaceIndexFromAxis(d, m.backFace);
        int sub = PickSubmesh(m.type, face);

        verts.Add(p0); verts.Add(p1); verts.Add(p3); verts.Add(p2);

        Vector3 wVec = new Vector3(du[0], du[1], du[2]);
        Vector3 hVec = new Vector3(dv[0], dv[1], dv[2]);
        float wUV = wVec.magnitude;
        float hUV = hVec.magnitude;

        uvs.Add(new Vector2(0, 0));
        uvs.Add(new Vector2(0, hUV));
        uvs.Add(new Vector2(wUV, hUV));
        uvs.Add(new Vector2(wUV, 0));

        cols.Add(Color.white); cols.Add(Color.white); cols.Add(Color.white); cols.Add(Color.white);

        tris[sub].Add(vertIndex); tris[sub].Add(vertIndex + 1); tris[sub].Add(vertIndex + 2);
        tris[sub].Add(vertIndex); tris[sub].Add(vertIndex + 2); tris[sub].Add(vertIndex + 3);

        for (int j = 0; j < 4; j++) cVerts.Add(verts[verts.Count - 4 + j]);
        cTris.Add(cVertIndex); cTris.Add(cVertIndex + 1); cTris.Add(cVertIndex + 2);
        cTris.Add(cVertIndex); cTris.Add(cVertIndex + 2); cTris.Add(cVertIndex + 3);
        cVertIndex += 4; vertIndex += 4;
    }

    int FaceIndexFromAxis(int axis, bool back)
    {
        if (axis == 0) return back ? 4 : 5;
        if (axis == 1) return back ? 3 : 2;
        return back ? 0 : 1;
    }

    struct MaskCell
    {
        public bool valid;
        public BlockType type;
        public bool backFace;
        public MaskCell(BlockType t, bool back) { valid = true; type = t; backFace = back; }
        public override bool Equals(object obj) => obj is MaskCell o && Equals(o);
        public bool Equals(MaskCell o) => valid == o.valid && (!valid || (type == o.type && backFace == o.backFace));
        
        // JAVÍTÁS: GetHashCode implementálása a figyelmeztetés eltüntetéséhez
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + valid.GetHashCode();
                if (valid)
                {
                    hash = hash * 23 + type.GetHashCode();
                    hash = hash * 23 + backFace.GetHashCode();
                }
                return hash;
            }
        }
    }

    bool IsGreedy(BlockType bt)
    {
        switch (bt)
        {
            case BlockType.Stone: case BlockType.Dirt: case BlockType.Sand:
            case BlockType.Gravel: case BlockType.Clay: case BlockType.Bedrock:
            case BlockType.CoalOre: case BlockType.IronOre: case BlockType.GoldOre:
            case BlockType.RedstoneOre: case BlockType.LapisOre: case BlockType.DiamondOre: case BlockType.EmeraldOre:
                return true;
            default: return false;
        }
    }

    void AddCrossMesh(int x, int y, int z, int sub, ref int vertIndex, Color tint)
    {
        verts.Add(new Vector3(x + 0.15f, y, z + 0.15f)); uvs.Add(new Vector2(0, 0)); cols.Add(tint);
        verts.Add(new Vector3(x + 0.85f, y, z + 0.85f)); uvs.Add(new Vector2(1, 0)); cols.Add(tint);
        verts.Add(new Vector3(x + 0.85f, y + 1, z + 0.85f)); uvs.Add(new Vector2(1, 1)); cols.Add(tint);
        verts.Add(new Vector3(x + 0.15f, y + 1, z + 0.15f)); uvs.Add(new Vector2(0, 1)); cols.Add(tint);

        verts.Add(new Vector3(x + 0.15f, y, z + 0.85f)); uvs.Add(new Vector2(0, 0)); cols.Add(tint);
        verts.Add(new Vector3(x + 0.85f, y, z + 0.15f)); uvs.Add(new Vector2(1, 0)); cols.Add(tint);
        verts.Add(new Vector3(x + 0.85f, y + 1, z + 0.15f)); uvs.Add(new Vector2(1, 1)); cols.Add(tint);
        verts.Add(new Vector3(x + 0.15f, y + 1, z + 0.85f)); uvs.Add(new Vector2(0, 1)); cols.Add(tint);

        tris[sub].Add(vertIndex + 0); tris[sub].Add(vertIndex + 2); tris[sub].Add(vertIndex + 1);
        tris[sub].Add(vertIndex + 0); tris[sub].Add(vertIndex + 3); tris[sub].Add(vertIndex + 2);
        tris[sub].Add(vertIndex + 1); tris[sub].Add(vertIndex + 2); tris[sub].Add(vertIndex + 0);
        tris[sub].Add(vertIndex + 2); tris[sub].Add(vertIndex + 3); tris[sub].Add(vertIndex + 0);

        tris[sub].Add(vertIndex + 4); tris[sub].Add(vertIndex + 6); tris[sub].Add(vertIndex + 5);
        tris[sub].Add(vertIndex + 4); tris[sub].Add(vertIndex + 7); tris[sub].Add(vertIndex + 6);
        tris[sub].Add(vertIndex + 5); tris[sub].Add(vertIndex + 6); tris[sub].Add(vertIndex + 4);
        tris[sub].Add(vertIndex + 6); tris[sub].Add(vertIndex + 7); tris[sub].Add(vertIndex + 4);

        vertIndex += 8;
    }

    bool IsTransparent(BlockType bt) => bt == BlockType.OakLeaves || bt == BlockType.BirchLeaves || bt == BlockType.SpruceLeaves || bt == BlockType.JungleLeaves || bt == BlockType.AcaciaLeaves || bt == BlockType.Water || bt == BlockType.Cactus || bt == BlockType.DeadBush;
    bool IsLeaf(BlockType bt) => bt == BlockType.OakLeaves || bt == BlockType.BirchLeaves || bt == BlockType.SpruceLeaves || bt == BlockType.JungleLeaves || bt == BlockType.AcaciaLeaves;

    int PickSubmesh(BlockType bt, int face)
    {
        bool isTB = (face == 2 || face == 3);
        switch (bt)
        {
            case BlockType.Grass: return (face == 2) ? SM_GrassTop : (face == 3 ? SM_Dirt : SM_GrassSide);
            case BlockType.Dirt: return SM_Dirt;
            case BlockType.Stone: return SM_Stone;
            case BlockType.Sand: return SM_Sand;
            case BlockType.Water: return SM_Water;
            case BlockType.Bedrock: return SM_Bedrock;
            case BlockType.Gravel: return SM_Gravel;
            case BlockType.Clay: return SM_Clay;
            case BlockType.OakLog: return isTB ? SM_OakLogTop : SM_OakLogSide;
            case BlockType.OakLeaves: return SM_OakLeaves;
            case BlockType.BirchLog: return isTB ? SM_BirchLogTop : SM_BirchLogSide;
            case BlockType.BirchLeaves: return SM_BirchLeaves;
            case BlockType.SpruceLog: return isTB ? SM_SpruceLogTop : SM_SpruceLogSide;
            case BlockType.SpruceLeaves: return SM_SpruceLeaves;
            case BlockType.JungleLog: return isTB ? SM_JungleLogTop : SM_JungleLogSide;
            case BlockType.JungleLeaves: return SM_JungleLeaves;
            case BlockType.AcaciaLog: return isTB ? SM_AcaciaLogTop : SM_AcaciaLogSide;
            case BlockType.AcaciaLeaves: return SM_AcaciaLeaves;
            case BlockType.Cactus: return (face == 2) ? SM_CactusTop : (face == 3 ? SM_CactusBottom : SM_CactusSide);
            case BlockType.DeadBush: return SM_DeadBush;
            case BlockType.CoalOre: return SM_Coal;
            case BlockType.IronOre: return SM_Iron;
            case BlockType.GoldOre: return SM_Gold;
            case BlockType.RedstoneOre: return SM_Redstone;
            case BlockType.LapisOre: return SM_Lapis;
            case BlockType.DiamondOre: return SM_Diamond;
            case BlockType.EmeraldOre: return SM_Emerald;
            default: return SM_Stone;
        }
    }

    BlockType GetNeighborBlock(Vector3Int local)
    {
        if (local.x < 0 || local.x >= 16 || local.z < 0 || local.z >= 16)
        {
            Vector3Int wp = LocalToWorld(local);
            return world.GetBlock(wp);
        }
        if (local.y < 0 || local.y >= 128) return BlockType.Air;
        return blocks[local.x, local.y, local.z];
    }
}