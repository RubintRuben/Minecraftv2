using System.Collections.Generic;
using UnityEngine;

public class VoxelWorld : MonoBehaviour
{
    public const int SeaLevel = 63;

    [System.Serializable]
    public class BiomeTints
    {
        public Color plainsGrass = new Color32(0x91, 0xBD, 0x59, 0xFF);
        public Color plainsFoliage = new Color32(0x77, 0xAB, 0x2F, 0xFF);
    }

    [System.Serializable]
    public class BlockMaterials
    {
        public Material grassTop;
        public Material grassSide;
        public Material dirt;
        public Material stone;
        public Material sand;
        public Material water;

        public Material logTop;
        public Material logSide;
        public Material leaves;

        public Material bedrock;

        public Material coalOre;
        public Material ironOre;
        public Material redstoneOre;
        public Material lapisOre;
        public Material diamondOre;
        public Material emeraldOre;
    }

    [System.Serializable]
    public class WorldSettings
    {
        public Transform player;
        public int viewDistanceInChunks = 6;
        public int seed = 12345;
    }

    public BlockMaterials materials;
    public BiomeTints biomeTints;
    public WorldSettings settings;
    public PhysicsMaterial terrainColliderMaterial;

    [Header("Terrain")]
    [Range(0f, 1f)] public float roughness = 0.25f;

    [Header("Water")]
    public bool minecraftLikeWater = true;
    public float oceanScale = 0.0018f;
    [Range(0f, 1f)] public float oceanThreshold = 0.52f;
    public int minWaterDepth = 2;

    [Header("Soil")]
    public int topSoilDepth = 4;

    [Header("Caves (banya)")]
    public bool enableCaves = true;
    public float caveScale = 0.045f;
    [Range(0.0f, 1.0f)] public float caveThreshold = 0.78f;
    public int caveMinDepthBelowSurface = 6;

    [Header("Sand Physics")]
    public bool sandFalls = true;
    public int sandChecksPerFrame = 64;
    public float fallingSandSpawnYOffset = 0.15f;
    public float fallingSandLinearDrag = 0.05f;
    public float fallingSandAngularDrag = 0.05f;

    const int ChunkCreateBudgetPerFrame = 2;

    const float BiomeTempScale = 0.0017f;
    const float BiomeRainScale = 0.0017f;
    const float BiomeForestScale = 0.0014f;

    private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    private readonly HashSet<Vector2Int> activeChunkCoords = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> neededChunks = new List<Vector2Int>(2048);

    private readonly Queue<Vector3Int> sandQueue = new Queue<Vector3Int>(4096);
    private readonly HashSet<Vector3Int> sandQueued = new HashSet<Vector3Int>();

    private enum BiomeId
    {
        Plains
    }

    private void Awake()
    {
        Random.InitState(settings.seed);
        if (biomeTints == null) biomeTints = new BiomeTints();
    }

    public void GetBiomeTintsAt(int worldX, int worldZ, out Color grass, out Color foliage)
    {
        BiomeId biome = BiomeId.Plains;
        switch (biome)
        {
            default:
                grass = biomeTints.plainsGrass;
                foliage = biomeTints.plainsFoliage;
                break;
        }
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
        neededChunks.Clear();

        int r = Mathf.Max(1, settings.viewDistanceInChunks);
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                Vector2Int c = new Vector2Int(playerChunk.x + dx, playerChunk.y + dz);
                activeChunkCoords.Add(c);
                if (!chunks.ContainsKey(c)) neededChunks.Add(c);
            }
        }

        neededChunks.Sort((a, b) =>
        {
            int da = (a.x - playerChunk.x) * (a.x - playerChunk.x) + (a.y - playerChunk.y) * (a.y - playerChunk.y);
            int db = (b.x - playerChunk.x) * (b.x - playerChunk.x) + (b.y - playerChunk.y) * (b.y - playerChunk.y);
            return da.CompareTo(db);
        });

        int budget = force ? neededChunks.Count : ChunkCreateBudgetPerFrame;
        for (int i = 0; i < neededChunks.Count && i < budget; i++)
            CreateChunk(neededChunks[i]);

        foreach (var kv in chunks)
        {
            bool shouldBeActive = activeChunkCoords.Contains(kv.Key);
            kv.Value.SetActive(shouldBeActive);
        }
    }

    private void CreateChunk(Vector2Int coord)
    {
        if (chunks.ContainsKey(coord)) return;

        GameObject go = new GameObject($"Chunk_{coord.x}_{coord.y}");
        go.transform.parent = transform;
        go.transform.position = new Vector3(coord.x * VoxelData.ChunkSize, 0, coord.y * VoxelData.ChunkSize);

        Chunk chunk = go.AddComponent<Chunk>();
        chunk.Init(this, coord, materials);

        chunks.Add(coord, chunk);

        RebuildChunkIfExists(new Vector2Int(coord.x - 1, coord.y));
        RebuildChunkIfExists(new Vector2Int(coord.x + 1, coord.y));
        RebuildChunkIfExists(new Vector2Int(coord.x, coord.y - 1));
        RebuildChunkIfExists(new Vector2Int(coord.x, coord.y + 1));
    }

    public Vector2Int WorldToChunkCoord(Vector3 worldPos)
    {
        int cx = Mathf.FloorToInt(worldPos.x / VoxelData.ChunkSize);
        int cz = Mathf.FloorToInt(worldPos.z / VoxelData.ChunkSize);
        return new Vector2Int(cx, cz);
    }

    public void GetBiomeParams(int worldX, int worldZ, out float temp, out float rain, out float forest, out float ocean)
    {
        int s = settings.seed;

        temp = Mathf.PerlinNoise((worldX + s * 11) * BiomeTempScale, (worldZ + s * 11) * BiomeTempScale);
        rain = Mathf.PerlinNoise((worldX + s * 23) * BiomeRainScale, (worldZ + s * 23) * BiomeRainScale);

        forest = Mathf.PerlinNoise((worldX + s * 37) * BiomeForestScale, (worldZ + s * 37) * BiomeForestScale);

        ocean = Mathf.PerlinNoise((worldX + s * 101) * oceanScale, (worldZ + s * 101) * oceanScale);
    }

    public int GetLandHeight(int worldX, int worldZ)
    {
        float baseScale = Mathf.Lerp(0.0018f, 0.0105f, roughness);
        float persistence = Mathf.Lerp(0.35f, 0.55f, roughness);
        float lacunarity = 2.0f;

        float h = 0f;
        float amp = 1f;
        float freq = 1f;

        int s = settings.seed;
        for (int i = 0; i < 4; i++)
        {
            float nx = (worldX + s * 17) * baseScale * freq;
            float nz = (worldZ + s * 17) * baseScale * freq;
            float n = Mathf.PerlinNoise(nx, nz) - 0.5f;
            h += n * amp;

            amp *= persistence;
            freq *= lacunarity;
        }

        GetBiomeParams(worldX, worldZ, out _, out _, out _, out float ocean);
        float continental = Mathf.Lerp(0.0f, -0.55f, Mathf.InverseLerp(oceanThreshold, 1.0f, ocean));
        h += continental;

        int height = SeaLevel + 6 + Mathf.RoundToInt(h * 60f);
        height = Mathf.Clamp(height, 2, VoxelData.ChunkHeight - 2);

        if (minecraftLikeWater && ocean < oceanThreshold)
        {
            if (height < SeaLevel - 1) height = SeaLevel - 1;
        }

        return height;
    }

    bool IsCave(int worldX, int worldY, int worldZ, int surfaceY)
    {
        if (!enableCaves) return false;
        if (worldY <= 5) return false;
        if (worldY >= surfaceY - caveMinDepthBelowSurface) return false;

        int s = settings.seed;

        float a = Mathf.PerlinNoise((worldX + s * 13) * caveScale, (worldZ + s * 13) * caveScale);
        float b = Mathf.PerlinNoise((worldX + s * 29) * caveScale, (worldY + s * 29) * caveScale);
        float c = Mathf.PerlinNoise((worldZ + s * 41) * caveScale, (worldY + s * 41) * caveScale);

        float v = (a + b + c) / 3f;

        float depth01 = Mathf.InverseLerp(surfaceY, 0, worldY);
        float t = Mathf.Lerp(caveThreshold + 0.06f, caveThreshold - 0.05f, depth01);

        return v > t;
    }

    public void GetOreSettings(out OreSettings o)
    {
        o = new OreSettings
        {
            coal = new OreLayer(countPerChunk: 20, veinSize: 17, yMin: 0, yMax: 127),
            iron = new OreLayer(countPerChunk: 20, veinSize: 9, yMin: 0, yMax: 63),
            redstone = new OreLayer(countPerChunk: 8, veinSize: 7, yMin: 0, yMax: 15),
            diamond = new OreLayer(countPerChunk: 1, veinSize: 8, yMin: 0, yMax: 15),
            lapis = new OreLayer(countPerChunk: 1, veinSize: 7, yMin: 0, yMax: 31),
            emerald = new OreLayer(countPerChunk: 11, veinSize: 1, yMin: 0, yMax: 32)
        };
    }

    public struct OreLayer
    {
        public int countPerChunk;
        public int veinSize;
        public int yMin;
        public int yMax;

        public OreLayer(int countPerChunk, int veinSize, int yMin, int yMax)
        {
            this.countPerChunk = countPerChunk;
            this.veinSize = veinSize;
            this.yMin = yMin;
            this.yMax = yMax;
        }
    }

    public struct OreSettings
    {
        public OreLayer coal, iron, redstone, lapis, diamond, emerald;
    }

    public BlockType GetGeneratedBlock(int worldX, int worldY, int worldZ)
    {
        if (worldY < 0 || worldY >= VoxelData.ChunkHeight) return BlockType.Air;

        if (worldY <= 4)
        {
            int maxY = 1 + (Mathf.Abs(Hash3(worldX, 0, worldZ)) % 5);
            if (worldY < maxY) return BlockType.Bedrock;
        }

        GetBiomeParams(worldX, worldZ, out _, out _, out _, out float ocean);
        int surfaceY = GetLandHeight(worldX, worldZ);

        if (worldY > surfaceY)
        {
            if (minecraftLikeWater && worldY <= SeaLevel && ocean >= oceanThreshold)
            {
                int depth = SeaLevel - surfaceY;
                if (depth >= minWaterDepth) return BlockType.Water;
            }
            return BlockType.Air;
        }

        bool underwaterOcean = minecraftLikeWater && ocean >= oceanThreshold && surfaceY < SeaLevel;

        if (worldY == surfaceY)
        {
            if (underwaterOcean) return BlockType.Sand;
            return BlockType.Grass;
        }

        int depthBelow = surfaceY - worldY;

        if (depthBelow <= Mathf.Max(1, topSoilDepth))
        {
            if (underwaterOcean && depthBelow <= 2) return BlockType.Sand;
            return BlockType.Dirt;
        }

        if (IsCave(worldX, worldY, worldZ, surfaceY))
            return BlockType.Air;

        return BlockType.Stone;
    }

    public BlockType GetBlock(Vector3Int worldPos)
    {
        if (worldPos.y < 0 || worldPos.y >= VoxelData.ChunkHeight) return BlockType.Air;

        Vector2Int c = new Vector2Int(
            Mathf.FloorToInt(worldPos.x / (float)VoxelData.ChunkSize),
            Mathf.FloorToInt(worldPos.z / (float)VoxelData.ChunkSize)
        );

        if (!chunks.TryGetValue(c, out Chunk chunk))
            return GetGeneratedBlock(worldPos.x, worldPos.y, worldPos.z);

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

    public bool RandomChance(int worldX, int worldY, int worldZ, int mod, int lessThan)
    {
        int h = Hash3(worldX * 92837111, worldY * 689287499, worldZ * 283923481);
        int v = Mathf.Abs(h) % mod;
        return v < lessThan;
    }
}
