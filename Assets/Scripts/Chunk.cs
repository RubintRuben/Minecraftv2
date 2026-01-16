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

        meshRenderer.sharedMaterials = new Material[]
        {
            mats.grassTop,
            mats.grassSide,
            mats.dirt,
            mats.logTop,
            mats.logSide,
            mats.leaves
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
        for (int x = 0; x < VoxelData.ChunkSize; x++)
        {
            for (int z = 0; z < VoxelData.ChunkSize; z++)
            {
                int wx = coord.x * VoxelData.ChunkSize + x;
                int wz = coord.y * VoxelData.ChunkSize + z;

                int height = world.GetHeight(wx, wz);

                for (int y = 0; y < VoxelData.ChunkHeight; y++)
                {
                    if (y > height) blocks[x, y, z] = BlockType.Air;
                    else if (y == height) blocks[x, y, z] = BlockType.Grass;
                    else blocks[x, y, z] = BlockType.Dirt;
                }
            }
        }

        for (int x = 2; x < VoxelData.ChunkSize - 2; x++)
        {
            for (int z = 2; z < VoxelData.ChunkSize - 2; z++)
            {
                int wx = coord.x * VoxelData.ChunkSize + x;
                int wz = coord.y * VoxelData.ChunkSize + z;

                int groundY = world.GetHeight(wx, wz);
                if (groundY <= 1 || groundY >= VoxelData.ChunkHeight - 10) continue;

                if (!world.ShouldPlaceTree(wx, wz)) continue;
                if (blocks[x, groundY, z] != BlockType.Grass) continue;

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
        List<Vector3> verts = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();

        List<int> trisGrassTop = new List<int>();
        List<int> trisGrassSide = new List<int>();
        List<int> trisDirt = new List<int>();
        List<int> trisLogTop = new List<int>();
        List<int> trisLogSide = new List<int>();
        List<int> trisLeaves = new List<int>();

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
                        Vector3Int neighbor = local + Vector3Int.RoundToInt(VoxelData.FaceChecks[face]);
                        BlockType nb = GetNeighborBlock(neighbor);

                        if (nb != BlockType.Air) continue;

                        for (int i = 0; i < 4; i++)
                        {
                            int v = VoxelData.Tris[face, i];
                            verts.Add(new Vector3(x, y, z) + VoxelData.Verts[v]);

                            Vector2 uv = VoxelData.BaseUVs[i];

                            if (face == 2 || face == 3)
                            {
                                uv = RotUV(uv, topRot);
                            }
                            else
                            {
                                if (sideFlip) uv = FlipU(uv);
                            }

                            uvs.Add(uv);
                        }

                        List<int> triList = PickTriList(bt, face, trisGrassTop, trisGrassSide, trisDirt, trisLogTop, trisLogSide, trisLeaves);

                        triList.Add(vertIndex + 0);
                        triList.Add(vertIndex + 1);
                        triList.Add(vertIndex + 2);
                        triList.Add(vertIndex + 0);
                        triList.Add(vertIndex + 2);
                        triList.Add(vertIndex + 3);

                        vertIndex += 4;
                    }
                }
            }
        }

        Mesh m = new Mesh();
        m.indexFormat = (verts.Count > 65000) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;

        m.SetVertices(verts);
        m.SetUVs(0, uvs);

        m.subMeshCount = 6;
        m.SetTriangles(trisGrassTop, 0);
        m.SetTriangles(trisGrassSide, 1);
        m.SetTriangles(trisDirt, 2);
        m.SetTriangles(trisLogTop, 3);
        m.SetTriangles(trisLogSide, 4);
        m.SetTriangles(trisLeaves, 5);

        m.RecalculateNormals();
        m.RecalculateBounds();

        meshFilter.sharedMesh = m;
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = m;
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

    private Vector2 FlipU(Vector2 uv)
    {
        return new Vector2(1f - uv.x, uv.y);
    }

    private Vector2 RotUV(Vector2 uv, int rot)
    {
        float u = uv.x;
        float v = uv.y;

        if (rot == 0) return new Vector2(u, v);
        if (rot == 1) return new Vector2(v, 1f - u);
        if (rot == 2) return new Vector2(1f - u, 1f - v);
        return new Vector2(1f - v, u);
    }

    private List<int> PickTriList(
        BlockType bt,
        int face,
        List<int> grassTop,
        List<int> grassSide,
        List<int> dirt,
        List<int> logTop,
        List<int> logSide,
        List<int> leaves)
    {
        if (bt == BlockType.Dirt) return dirt;

        if (bt == BlockType.Grass)
        {
            if (face == 2) return grassTop;
            if (face == 3) return dirt;
            return grassSide;
        }

        if (bt == BlockType.Log)
        {
            if (face == 2 || face == 3) return logTop;
            return logSide;
        }

        if (bt == BlockType.Leaves) return leaves;

        return dirt;
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
