using System.Collections.Generic;
using UnityEngine;

public class VoxelWorld : MonoBehaviour
{
    [Header("Materials (assign in Inspector)")]
    public Material matGrassTop;
    public Material matGrassSide;
    public Material matDirt;

    [Header("World Settings")]
    public Transform player;
    public int viewDistanceInChunks = 6;
    public int seed = 12345;

    [Header("Terrain Noise")]
    public float noiseScale = 0.03f;
    public int baseHeight = 24;
    public int heightAmplitude = 16;

    private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    private readonly HashSet<Vector2Int> activeChunkCoords = new HashSet<Vector2Int>();

    private void Awake()
    {
        Random.InitState(seed);
    }

    private void Start()
    {
        UpdateWorld(force: true);
    }

    private void Update()
    {
        UpdateWorld(force: false);
    }

    private void UpdateWorld(bool force)
    {
        if (player == null) return;

        Vector2Int playerChunk = WorldToChunkCoord(player.position);

        activeChunkCoords.Clear();
        for (int x = -viewDistanceInChunks; x <= viewDistanceInChunks; x++)
        {
            for (int z = -viewDistanceInChunks; z <= viewDistanceInChunks; z++)
            {
                Vector2Int coord = new Vector2Int(playerChunk.x + x, playerChunk.y + z);
                activeChunkCoords.Add(coord);

                if (!chunks.ContainsKey(coord))
                    CreateChunk(coord);
                else if (force)
                    chunks[coord].SetActive(true);
            }
        }

        foreach (var kv in chunks)
        {
            bool shouldBeActive = activeChunkCoords.Contains(kv.Key);
            kv.Value.SetActive(shouldBeActive);
        }
    }

    private void CreateChunk(Vector2Int coord)
    {
        GameObject go = new GameObject($"Chunk_{coord.x}_{coord.y}");
        go.transform.parent = transform;
        go.transform.position = new Vector3(coord.x * VoxelData.ChunkSize, 0, coord.y * VoxelData.ChunkSize);

        Chunk chunk = go.AddComponent<Chunk>();
        chunk.Init(this, coord, matGrassTop, matGrassSide, matDirt);

        chunks.Add(coord, chunk);
    }

    public Vector2Int WorldToChunkCoord(Vector3 worldPos)
    {
        int cx = Mathf.FloorToInt(worldPos.x / VoxelData.ChunkSize);
        int cz = Mathf.FloorToInt(worldPos.z / VoxelData.ChunkSize);
        return new Vector2Int(cx, cz);
    }

    public int GetHeight(int worldX, int worldZ)
    {
        float nx = (worldX + seed) * noiseScale;
        float nz = (worldZ + seed) * noiseScale;
        float n = Mathf.PerlinNoise(nx, nz);
        int h = baseHeight + Mathf.RoundToInt(n * heightAmplitude);
        h = Mathf.Clamp(h, 1, VoxelData.ChunkHeight - 1);
        return h;
    }

    public BlockType GetBlock(Vector3Int worldPos)
    {
        if (worldPos.y < 0 || worldPos.y >= VoxelData.ChunkHeight) return BlockType.Air;

        Vector2Int c = new Vector2Int(
            Mathf.FloorToInt(worldPos.x / (float)VoxelData.ChunkSize),
            Mathf.FloorToInt(worldPos.z / (float)VoxelData.ChunkSize)
        );

        if (!chunks.TryGetValue(c, out Chunk chunk)) return BlockType.Air;

        Vector3Int local = chunk.WorldToLocal(worldPos);
        return chunk.GetBlockLocal(local);
    }

    public void SetBlock(Vector3Int worldPos, BlockType type)
    {
        if (worldPos.y < 0 || worldPos.y >= VoxelData.ChunkHeight) return;

        Vector2Int c = new Vector2Int(
            Mathf.FloorToInt(worldPos.x / (float)VoxelData.ChunkSize),
            Mathf.FloorToInt(worldPos.z / (float)VoxelData.ChunkSize)
        );

        if (!chunks.TryGetValue(c, out Chunk chunk)) return;

        Vector3Int local = chunk.WorldToLocal(worldPos);
        chunk.SetBlockLocal(local, type);
        chunk.Rebuild();

        if (local.x == 0) RebuildChunkIfExists(new Vector2Int(c.x - 1, c.y));
        if (local.x == VoxelData.ChunkSize - 1) RebuildChunkIfExists(new Vector2Int(c.x + 1, c.y));
        if (local.z == 0) RebuildChunkIfExists(new Vector2Int(c.x, c.y - 1));
        if (local.z == VoxelData.ChunkSize - 1) RebuildChunkIfExists(new Vector2Int(c.x, c.y + 1));
    }

    private void RebuildChunkIfExists(Vector2Int coord)
    {
        if (chunks.TryGetValue(coord, out Chunk ch))
            ch.Rebuild();
    }
}
