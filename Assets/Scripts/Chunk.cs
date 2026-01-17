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
            mats.logTop,
            mats.logSide,
            mats.leaves,
            mats.sand,
            mats.water
        };

        blocks = new BlockType[VoxelData.ChunkSize, VoxelData.ChunkHeight, VoxelData.ChunkSize];

        Generate();
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

    private void Generate()
    {
        int wl = VoxelWorld.FixedWaterLevel;
        bool wEnabled = (world.water != null && world.water.enabled);

        for (int x = 0; x < VoxelData.ChunkSize; x++)
        {
            for (int z = 0; z < VoxelData.ChunkSize; z++)
            {
                int wx = coord.x * VoxelData.ChunkSize + x;
                int wz = coord.y * VoxelData.ChunkSize + z;

                int landH = world.GetLandHeight(wx, wz);

                bool waterCol = wEnabled && world.IsWaterColumn(wx, wz);
                int depth = waterCol ? world.GetWaterDepth(wx, wz) : 0;
                int bottom = waterCol ? Mathf.Clamp(wl - depth, 1, wl - 2) : int.MaxValue;

                int groundY = waterCol ? bottom : landH;

                bool beach = false;
                if (!waterCol && wEnabled && world.IsNearWater(wx, wz, world.water.beachRadius))
                {
                    if (world.ShouldBeachSand(wx, wz)) beach = true;
                }

                int dune = 0;
                if (waterCol)
                {
                    float bx = (wx + world.settings.seed * 211) * world.water.bedNoiseScale;
                    float bz = (wz + world.settings.seed * 211) * world.water.bedNoiseScale;
                    float b = Mathf.PerlinNoise(bx, bz) - 0.5f;
                    dune = Mathf.RoundToInt(b * 2f * Mathf.Max(0, world.water.bedDuneAmp));
                }

                int sandTop = waterCol ? Mathf.Clamp(bottom + dune, 1, wl - 2) : groundY;

                for (int y = 0; y < VoxelData.ChunkHeight; y++)
                {
                    if (y > groundY)
                    {
                        if (waterCol && y <= wl) blocks[x, y, z] = BlockType.Water;
                        else blocks[x, y, z] = BlockType.Air;
                    }
                    else if (y == groundY)
                    {
                        if (waterCol)
                        {
                            blocks[x, y, z] = (y <= sandTop) ? BlockType.Sand : BlockType.Dirt;
                        }
                        else
                        {
                            blocks[x, y, z] = beach ? BlockType.Sand : BlockType.Grass;
                        }
                    }
                    else
                    {
                        if (waterCol) blocks[x, y, z] = BlockType.Sand;
                        else blocks[x, y, z] = BlockType.Dirt;
                    }
                }
            }
        }

        for (int x = 2; x < VoxelData.ChunkSize - 2; x++)
        {
            for (int z = 2; z < VoxelData.ChunkSize - 2; z++)
            {
                int wx = coord.x * VoxelData.ChunkSize + x;
                int wz = coord.y * VoxelData.ChunkSize + z;

                int groundY = -1;
                for (int y = VoxelData.ChunkHeight - 2; y >= 1; y--)
                {
                    BlockType bt = blocks[x, y, z];
                    if (bt != BlockType.Air && bt != BlockType.Water)
                    {
                        groundY = y;
                        break;
                    }
                }

                if (groundY <= 1 || groundY >= VoxelData.ChunkHeight - 10) continue;
                if (!world.ShouldPlaceTree(wx, wz)) continue;
                if (blocks[x, groundY, z] != BlockType.Grass) continue;
                if (world.IsWaterColumn(wx, wz)) continue;

                int trunkH = world.GetTreeTrunkHeight(wx, wz);

                for (int i = 1; i <= trunkH; i++)
                {
                    int y = groundY + i;
                    if (y < 0 || y >= VoxelData.ChunkHeight) break;
                    blocks[x, y, z] = BlockType.Log;
                }

                int topY = groundY + trunkH;
                int r = 2;

                for (int lx = -r; lx <= r; lx++)
                {
                    for (int lz = -r; lz <= r; lz++)
                    {
                        for (int ly = -r; ly <= r; ly++)
                        {
                            int ax = x + lx;
                            int az = z + lz;
                            int ay = topY + ly;

                            if (ax < 0 || ax >= VoxelData.ChunkSize) continue;
                            if (az < 0 || az >= VoxelData.ChunkSize) continue;
                            if (ay < 0 || ay >= VoxelData.ChunkHeight) continue;

                            float d = Mathf.Abs(lx) + Mathf.Abs(lz) + Mathf.Abs(ly) * 1.25f;
                            if (d > 4.2f) continue;

                            if (blocks[ax, ay, az] == BlockType.Air)
                                blocks[ax, ay, az] = BlockType.Leaves;
                        }
                    }
                }
            }
        }
    }

    public void Rebuild()
    {
        List<Vector3> verts = new List<Vector3>(8192);
        List<Vector2> uvs = new List<Vector2>(8192);

        List<int>[] tris = new List<int>[8];
        for (int i = 0; i < 8; i++) tris[i] = new List<int>(8192);

        List<Vector3> cVerts = new List<Vector3>(8192);
        List<int> cTris = new List<int>(8192);
        int cVertIndex = 0;

        int vertIndex = 0;

        for (int x = 0; x < VoxelData.ChunkSize; x++)
        {
            for (int y = 0; y < VoxelData.ChunkHeight; y++)
            {
                for (int z = 0; z < VoxelData.ChunkSize; z++)
                {
                    BlockType bt = blocks[x, y, z];
                    if (bt == BlockType.Air) continue;

                    Vector3Int wpos = LocalToWorld(new Vector3Int(x, y, z));
                    int topRot = YawFor(wpos);
                    bool sideFlip = SideFlipFor(wpos);

                    Vector3Int local = new Vector3Int(x, y, z);

                    for (int face = 0; face < 6; face++)
                    {
                        if (bt == BlockType.Water)
                        {
                            if (face != 2) continue;
                            BlockType above = GetNeighborBlock(local + Vector3Int.up);
                            if (above == BlockType.Water) continue;
                        }
                        else
                        {
                            Vector3Int neighbor = local + Vector3Int.RoundToInt(VoxelData.FaceChecks[face]);
                            BlockType nb = GetNeighborBlock(neighbor);
                            if (nb != BlockType.Air && nb != BlockType.Water) continue;
                        }

                        for (int i = 0; i < 4; i++)
                        {
                            int v = VoxelData.Tris[face, i];
                            verts.Add(new Vector3(x, y, z) + VoxelData.Verts[v]);

                            Vector2 uv = VoxelData.BaseUVs[i];

                            if (bt != BlockType.Water)
                            {
                                if (face == 2 || face == 3) uv = RotUV(uv, topRot);
                                else if (sideFlip) uv = FlipU(uv);
                            }

                            uvs.Add(uv);
                        }

                        int sub = PickSubmesh(bt, face);

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
            }
        }

        Mesh m = new Mesh();
        m.indexFormat = (verts.Count > 65000) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        m.SetVertices(verts);
        m.SetUVs(0, uvs);
        m.subMeshCount = 8;
        for (int i = 0; i < 8; i++) m.SetTriangles(tris[i], i);
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
        if (bt == BlockType.Water) return 7;
        if (bt == BlockType.Sand) return 6;
        if (bt == BlockType.Leaves) return 5;

        if (bt == BlockType.Dirt) return 2;

        if (bt == BlockType.Grass)
        {
            if (face == 2) return 0;
            if (face == 3) return 2;
            return 1;
        }

        if (bt == BlockType.Log)
        {
            if (face == 2 || face == 3) return 3;
            return 4;
        }

        return 2;
    }

    private int YawFor(Vector3Int worldPos)
    {
        int h = world.Hash3(worldPos.x, 0, worldPos.z);
        return Mathf.Abs(h) & 3;
    }

    private bool SideFlipFor(Vector3Int worldPos)
    {
        int h = world.Hash3(worldPos.x, 1, worldPos.z);
        return (Mathf.Abs(h) & 1) == 1;
    }

    private Vector2 FlipU(Vector2 uv) => new Vector2(1f - uv.x, uv.y);

    private Vector2 RotUV(Vector2 uv, int rot)
    {
        float u = uv.x;
        float v = uv.y;

        if (rot == 0) return new Vector2(u, v);
        if (rot == 1) return new Vector2(v, 1f - u);
        if (rot == 2) return new Vector2(1f - u, 1f - v);
        return new Vector2(1f - v, u);
    }

    private BlockType GetNeighborBlock(Vector3Int neighborLocal)
    {
        if (neighborLocal.x >= 0 && neighborLocal.x < VoxelData.ChunkSize &&
            neighborLocal.z >= 0 && neighborLocal.z < VoxelData.ChunkSize &&
            neighborLocal.y >= 0 && neighborLocal.y < VoxelData.ChunkHeight)
        {
            return blocks[neighborLocal.x, neighborLocal.y, neighborLocal.z];
        }

        Vector3Int worldPos = LocalToWorld(neighborLocal);
        return world.GetBlock(worldPos);
    }
}
