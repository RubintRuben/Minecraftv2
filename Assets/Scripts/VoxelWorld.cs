using System.Collections.Generic;
using UnityEngine;

public class VoxelWorld : MonoBehaviour
{
    public const int FixedWaterLevel = 62;

    [System.Serializable]
    public class BlockMaterials
    {
        public Material grassTop;
        public Material grassSide;
        public Material dirt;
        public Material logTop;
        public Material logSide;
        public Material leaves;
        public Material sand;
        public Material water;
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
        public float noiseScale = 0.02f;
        public int heightAmplitude = 26;
        public float detailScale = 0.06f;
        public int detailAmp = 8;
    }

    [System.Serializable]
    public class Trees
    {
        public bool enabled = true;
        public int chancePercent = 3;
        public int minTrunkHeight = 4;
        public int maxTrunkHeight = 6;
    }

    [System.Serializable]
    public class WaterGen
    {
        public bool enabled = true;

        public float presenceNoiseScale = 0.0035f;
        [Range(0f, 1f)] public float presenceCutoff = 0.76f;

        public float lakeNoiseScale = 0.006f;
        [Range(0f, 1f)] public float lakeThreshold = 0.80f;

        public float riverNoiseScale = 0.011f;
        [Range(0.001f, 0.08f)] public float riverWidth = 0.012f;

        public int lakeMinDepth = 8;
        public int lakeMaxDepth = 18;
        public int riverMinDepth = 3;
        public int riverMaxDepth = 7;

        public float bedNoiseScale = 0.09f;
        public int bedDuneAmp = 2;

        public int beachRadius = 4;
        [Range(0f, 1f)] public float beachChance = 0.28f;
    }

    [Header("Materials")]
    public BlockMaterials materials;

    [Header("World Settings")]
    public WorldSettings settings;
    public TerrainNoise terrain;
    public Trees trees;
    public WaterGen water;

    [Header("Physics")]
    public PhysicsMaterial terrainColliderMaterial;

    [Header("Sand Physics")]
    public bool sandFalls = true;
    public int sandChecksPerFrame = 64;
    public float fallingSandSpawnYOffset = 0.15f;
    public float fallingSandLinearDrag = 0.05f;
    public float fallingSandAngularDrag = 0.05f;

    private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    private readonly HashSet<Vector2Int> activeChunkCoords = new HashSet<Vector2Int>();

    private readonly Queue<Vector3Int> sandQueue = new Queue<Vector3Int>(4096);
    private readonly HashSet<Vector3Int> sandQueued = new HashSet<Vector3Int>();

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
            if (pc != null) pc.PlaceAboveGround(new Vector3(0.5f, 0f, 0.5f), 300f, 0.2f);
            else
            {
                int h = GetLandHeight(0, 0);
                settings.player.position = new Vector3(0.5f, h + 3f, 0.5f);
            }
        }

        UpdateWorld(true);
    }

    private void Update()
    {
        UpdateWorld(false);
        TickSand();
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

                if (!chunks.ContainsKey(coord)) CreateChunk(coord);
                else if (force) chunks[coord].SetActive(true);
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

    public int GetLandHeight(int worldX, int worldZ)
    {
        float nx = (worldX + settings.seed) * terrain.noiseScale;
        float nz = (worldZ + settings.seed) * terrain.noiseScale;
        float n1 = Mathf.PerlinNoise(nx, nz);

        float dx = (worldX + settings.seed * 7) * terrain.detailScale;
        float dz = (worldZ + settings.seed * 7) * terrain.detailScale;
        float n2 = Mathf.PerlinNoise(dx, dz) - 0.5f;

        int h = FixedWaterLevel + 1 + Mathf.RoundToInt((n1 - 0.5f) * 2f * terrain.heightAmplitude) + Mathf.RoundToInt(n2 * terrain.detailAmp);
        h = Mathf.Clamp(h, 1, VoxelData.ChunkHeight - 2);
        return h;
    }

    float Presence(int worldX, int worldZ)
    {
        if (water == null) return 0f;
        float px = (worldX + settings.seed * 97) * water.presenceNoiseScale;
        float pz = (worldZ + settings.seed * 97) * water.presenceNoiseScale;
        return Mathf.PerlinNoise(px, pz);
    }

    public bool IsWaterColumn(int worldX, int worldZ)
    {
        if (water == null || !water.enabled) return false;

        if (Presence(worldX, worldZ) < water.presenceCutoff) return false;

        float lx = (worldX + settings.seed * 13) * water.lakeNoiseScale;
        float lz = (worldZ + settings.seed * 13) * water.lakeNoiseScale;
        float lake = Mathf.PerlinNoise(lx, lz);

        float rx = (worldX + settings.seed * 29) * water.riverNoiseScale;
        float rz = (worldZ + settings.seed * 29) * water.riverNoiseScale;
        float river = Mathf.PerlinNoise(rx, rz);

        bool isLake = lake > water.lakeThreshold;
        bool isRiver = Mathf.Abs(river - 0.5f) < water.riverWidth;

        return isLake || isRiver;
    }

    public int GetWaterDepth(int worldX, int worldZ)
    {
        if (water == null || !water.enabled) return 0;
        if (!IsWaterColumn(worldX, worldZ)) return 0;

        float lx = (worldX + settings.seed * 13) * water.lakeNoiseScale;
        float lz = (worldZ + settings.seed * 13) * water.lakeNoiseScale;
        float lake = Mathf.PerlinNoise(lx, lz);

        float rx = (worldX + settings.seed * 29) * water.riverNoiseScale;
        float rz = (worldZ + settings.seed * 29) * water.riverNoiseScale;
        float river = Mathf.PerlinNoise(rx, rz);

        bool isLake = lake > water.lakeThreshold;
        bool isRiver = Mathf.Abs(river - 0.5f) < water.riverWidth;

        float sx = (worldX + settings.seed * 101) * 0.02f;
        float sz = (worldZ + settings.seed * 101) * 0.02f;
        float shape = Mathf.PerlinNoise(sx, sz);

        int d = isLake
            ? Mathf.RoundToInt(Mathf.Lerp(water.lakeMinDepth, water.lakeMaxDepth, shape))
            : Mathf.RoundToInt(Mathf.Lerp(water.riverMinDepth, water.riverMaxDepth, shape));

        if (isLake && isRiver)
        {
            int dl = Mathf.RoundToInt(Mathf.Lerp(water.lakeMinDepth, water.lakeMaxDepth, shape));
            int dr = Mathf.RoundToInt(Mathf.Lerp(water.riverMinDepth, water.riverMaxDepth, shape));
            d = Mathf.RoundToInt((dl + dr) * 0.5f);
        }

        float bx = (worldX + settings.seed * 211) * water.bedNoiseScale;
        float bz = (worldZ + settings.seed * 211) * water.bedNoiseScale;
        float b = Mathf.PerlinNoise(bx, bz) - 0.5f;
        d += Mathf.RoundToInt(b * 2f * Mathf.Max(0, water.bedDuneAmp));

        d = Mathf.Clamp(d, 3, FixedWaterLevel - 2);
        return d;
    }

    public bool IsNearWater(int worldX, int worldZ, int radius)
    {
        int r = Mathf.Max(1, radius);
        for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
                if (IsWaterColumn(worldX + dx, worldZ + dz)) return true;
        return false;
    }

    public bool ShouldBeachSand(int worldX, int worldZ)
    {
        if (water == null) return false;
        float bx = (worldX + settings.seed * 401) * 0.09f;
        float bz = (worldZ + settings.seed * 401) * 0.09f;
        float n = Mathf.PerlinNoise(bx, bz);
        return n > (1f - Mathf.Clamp01(water.beachChance));
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

        BlockType prev = GetBlock(worldPos);

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

        if (sandFalls)
        {
            if (type == BlockType.Sand) EnqueueSand(worldPos);
            if (prev != BlockType.Air && type == BlockType.Air)
            {
                Vector3Int above = new Vector3Int(worldPos.x, worldPos.y + 1, worldPos.z);
                if (GetBlock(above) == BlockType.Sand) EnqueueSand(above);
            }
        }
    }

    private void RebuildChunkIfExists(Vector2Int coord)
    {
        if (chunks.TryGetValue(coord, out Chunk ch))
            ch.Rebuild();
    }

    private void EnqueueSand(Vector3Int pos)
    {
        if (!sandQueued.Add(pos)) return;
        sandQueue.Enqueue(pos);
    }

    private void TickSand()
    {
        if (!sandFalls) return;
        int n = Mathf.Max(1, sandChecksPerFrame);

        for (int i = 0; i < n; i++)
        {
            if (sandQueue.Count == 0) break;

            Vector3Int p = sandQueue.Dequeue();
            sandQueued.Remove(p);

            if (GetBlock(p) != BlockType.Sand) continue;

            Vector3Int below = new Vector3Int(p.x, p.y - 1, p.z);
            if (below.y < 0) continue;

            BlockType b = GetBlock(below);
            if (b != BlockType.Air && b != BlockType.Water) continue;

            SpawnFallingSandEntity(p);
            SetBlock(p, BlockType.Air);
        }
    }

    private void SpawnFallingSandEntity(Vector3Int p)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "FallingSand";
        go.transform.position = new Vector3(p.x + 0.5f, p.y + 0.5f + fallingSandSpawnYOffset, p.z + 0.5f);

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null && materials != null && materials.sand != null) mr.sharedMaterial = materials.sand;

        var rb = go.AddComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearDamping = fallingSandLinearDrag;
        rb.angularDamping = fallingSandAngularDrag;

        var fs = go.AddComponent<FallingSandEntity>();
        fs.world = this;
    }
}
