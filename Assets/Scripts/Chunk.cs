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

    private Material matGrassTop;
    private Material matGrassSide;
    private Material matDirt;

    public void Init(VoxelWorld world, Vector2Int coord, Material grassTop, Material grassSide, Material dirt)
    {
        this.world = world;
        this.coord = coord;

        matGrassTop = grassTop;
        matGrassSide = grassSide;
        matDirt = dirt;

        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();

        meshRenderer.sharedMaterials = new Material[] { matGrassTop, matGrassSide, matDirt };

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
    }

    public void Rebuild()
    {
        List<Vector3> verts = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();

        List<int> trisGrassTop = new List<int>();  
        List<int> trisGrassSide = new List<int>(); 
        List<int> trisDirt = new List<int>();      

        int vertIndex = 0;

        for (int x = 0; x < VoxelData.ChunkSize; x++)
        {
            for (int y = 0; y < VoxelData.ChunkHeight; y++)
            {
                for (int z = 0; z < VoxelData.ChunkSize; z++)
                {
                    BlockType bt = blocks[x, y, z];
                    if (bt == BlockType.Air) continue;

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
                            uvs.Add(VoxelData.BaseUVs[i]); // teljes textúra (0..1)
                        }

                        List<int> triList = PickTriList(bt, face, trisGrassTop, trisGrassSide, trisDirt);

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

        m.subMeshCount = 3;
        m.SetTriangles(trisGrassTop, 0);
        m.SetTriangles(trisGrassSide, 1);
        m.SetTriangles(trisDirt, 2);

        m.RecalculateNormals();
        m.RecalculateBounds();

        meshFilter.sharedMesh = m;
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = m;
    }

    private List<int> PickTriList(BlockType bt, int face, List<int> grassTop, List<int> grassSide, List<int> dirt)
    {
        if (bt == BlockType.Dirt) return dirt;

        if (bt == BlockType.Grass)
        {
            if (face == 2) return grassTop;

            if (face == 3) return dirt;

            return grassSide;
        }

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
