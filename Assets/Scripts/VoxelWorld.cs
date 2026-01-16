using System.Collections.Generic;
using UnityEngine;

public class VoxelWorld : MonoBehaviour
{
    [System.Serializable]
    public class BlockMaterials
    {
        public Material grassTop;
        public Material grassSide;
        public Material dirt;
        public Material logTop;
        public Material logSide;
        public Material leaves;
    }

    [System.Serializable]
    public class WorldSettings
    {
        public Transform player;
        public int viewDistanceInChunks = 6;
        public int seed = 12345;
    }

    [System.Serializable]
    public class TerrainNoise
    {
        public float noiseScale = 0.03f;
        public int baseHeight = 24;
        public int heightAmplitude = 16;
    }

    [System.Serializable]
    public class Trees
    {
        public bool enabled = true;
        public int chancePercent = 3;
        public int minTrunkHeight = 4;
        public int maxTrunkHeight = 6;
    }

    public BlockMaterials materials;
    public WorldSettings settings;
    public TerrainNoise terrain;
    public Trees trees;

    private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    private readonly HashSet<Vector2Int> activeChunkCoords = new HashSet<Vector2Int>();

    private void Awake()
    {
        Random.InitState(settings.seed);
    }

private void Start()
{
    UpdateWorld(true);

    if (settings.player != null)
    {
        var pc = settings.player.GetComponent<PlayerController>();
        if (pc != null)
        {
            pc.PlaceAboveGround(new Vector3(0.5f, 0f, 0.5f), 300f, 0.2f);
        }
        else
        {
            int h = GetHeight(0, 0);
            settings.player.position = new Vector3(0.5f, h + 3f, 0.5f);
        }
    }

    UpdateWorld(true);
}




    private void Update()
    {
        UpdateWorld(false);
    }

    private void UpdateWorld(bool force)
    {
        if (settings.player == null) return;

        Vector2Int playerChunk = WorldToChunkCoord(settings.player.position);

        activeChunkCoords.Clear();
        for (int x = -settings.viewDistanceInChunks; x <= settings.viewDistanceInChunks; x++)
        {
            for (int z = -settings.viewDistanceInChunks; z <= settings.viewDistanceInChunks; z++)
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
        chunk.Init(this, coord, materials);

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
        float nx = (worldX + settings.seed) * terrain.noiseScale;
        float nz = (worldZ + settings.seed) * terrain.noiseScale;
        float n = Mathf.PerlinNoise(nx, nz);
        int h = terrain.baseHeight + Mathf.RoundToInt(n * terrain.heightAmplitude);
        h = Mathf.Clamp(h, 1, VoxelData.ChunkHeight - 2);
        return h;
    }

    public bool ShouldPlaceTree(int worldX, int worldZ)
    {
        if (!trees.enabled) return false;

        int h = Hash(worldX, worldZ, settings.seed);
        int v = Mathf.Abs(h) % 100;
        return v < Mathf.Clamp(trees.chancePercent, 0, 100);
    }

    public int GetTreeTrunkHeight(int worldX, int worldZ)
    {
        int h = Hash(worldX * 73856093, worldZ * 19349663, settings.seed ^ 0x5bd1e995);
        int range = Mathf.Max(1, trees.maxTrunkHeight - trees.minTrunkHeight + 1);
        return trees.minTrunkHeight + (Mathf.Abs(h) % range);
    }

    private int Hash(int x, int z, int seed)
    {
        unchecked
        {
            int h = seed;
            h = (h * 397) ^ x;
            h = (h * 397) ^ z;
            h ^= (h << 13);
            h ^= (h >> 17);
            h ^= (h << 5);
            return h;
        }
    }

    public int Hash3(int x, int y, int z)
    {
        return Hash(x, z, Hash(y, x, settings.seed));
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
