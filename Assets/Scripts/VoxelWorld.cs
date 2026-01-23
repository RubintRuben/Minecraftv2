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

        public Material goldOre;
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

    [Header("Rivers")]
    public bool enableRivers = true;
    [Tooltip("Lower = longer, smoother rivers")]
    public float riverScale = 0.00125f;
    [Tooltip("River half-width in noise-space (smaller = thinner rivers)")]
    [Range(0.002f, 0.08f)] public float riverWidth = 0.020f;
    [Tooltip("How deep the river carves into terrain")]
    [Range(1f, 30f)] public float riverDepth = 11f;
    [Tooltip("Extra flattening around river banks")]
    [Range(0f, 1f)] public float riverBankBlend = 0.55f;
    [Tooltip("Rivers avoid strong oceans when this is > 0")]
    [Range(0f, 1f)] public float riverOceanAvoid = 0.55f;

    [Header("Soil")]
    public int topSoilDepth = 4;

    [Header("Caves (banya)")]
    public bool enableCaves = true;
    public float caveScale = 0.045f;
    [Range(0.0f, 1f)] public float caveThreshold = 0.78f;
    public int caveMinDepthBelowSurface = 6;

    [Header("Caves (better)")]
    [Tooltip("Spaghetti caves: lower = longer tunnels")]
    public float caveSpaghettiScale = 0.020f;
    [Range(0.0f, 1f)] public float caveSpaghettiThreshold = 0.62f;
    [Tooltip("Big caverns: lower = bigger blobs")]
    public float caveCavernScale = 0.010f;
    [Range(0.0f, 1f)] public float caveCavernThreshold = 0.74f;

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

    // =========================
    // Terrain generation
    // =========================

    float RiverDistance01(int worldX, int worldZ)
    {
        // 0 = center line, 1 = far away
        int s = settings.seed;

        // Cheap domain warp to avoid repetitive parallel bands
        float wx = worldX + (Mathf.PerlinNoise((worldX + s * 311) * 0.0025f, (worldZ + s * 733) * 0.0025f) - 0.5f) * 220f;
        float wz = worldZ + (Mathf.PerlinNoise((worldX + s * 911) * 0.0025f, (worldZ + s * 199) * 0.0025f) - 0.5f) * 220f;

        float n = Mathf.PerlinNoise((wx + s * 61) * riverScale, (wz + s * 61) * riverScale);
        float dist = Mathf.Abs(n - 0.5f) * 2f; // 0..1
        return Mathf.Clamp01(dist);
    }

    float RiverMask01(int worldX, int worldZ, float ocean)
    {
        if (!minecraftLikeWater || !enableRivers) return 0f;

        // Avoid placing rivers deep into oceans (but still allow them to meet the sea)
        float avoid = Mathf.InverseLerp(oceanThreshold, 1f, ocean);
        if (avoid > riverOceanAvoid) return 0f;

        float dist = RiverDistance01(worldX, worldZ);
        // Convert width into a distance threshold
        float width = Mathf.Clamp(riverWidth, 0.002f, 0.12f);
        float m = Mathf.InverseLerp(width, 0f, dist);

        // Sharpen a bit so we have a defined channel
        m = m * m;
        return Mathf.Clamp01(m);
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

        // Continents: pull down land where ocean noise is strong
        float continental = Mathf.Lerp(0.0f, -0.55f, Mathf.InverseLerp(oceanThreshold, 1.0f, ocean));
        h += continental;

        int height = SeaLevel + 6 + Mathf.RoundToInt(h * 60f);
        height = Mathf.Clamp(height, 2, VoxelData.ChunkHeight - 2);

        // Rivers carve the terrain (even inland). Minecraft rivers are around sea level.
        float riverMask = RiverMask01(worldX, worldZ, ocean);
        if (riverMask > 0f)
        {
            float carve = riverMask;
            carve = Mathf.Lerp(carve, 1f, riverBankBlend * carve); // wider banks

            int carved = height - Mathf.RoundToInt(riverDepth * carve);
            // Keep river channel close to sea level; allow small variation.
            carved = Mathf.Min(carved, SeaLevel - 1);
            height = Mathf.Clamp(carved, 2, VoxelData.ChunkHeight - 2);
        }

        // Keep inland from dipping too far below sea (prevents endless inland oceans)
        if (minecraftLikeWater && ocean < oceanThreshold)
        {
            if (height < SeaLevel - 1) height = SeaLevel - 1;
        }

        return height;
    }

    // =========================
    // Cave generation (improved)
    // =========================

    float Noise3Cheap(float x, float y, float z)
    {
        // Unity has only 2D Perlin; combine 3 planes to get a usable 3D-ish noise.
        float a = Mathf.PerlinNoise(x, z);
        float b = Mathf.PerlinNoise(x, y);
        float c = Mathf.PerlinNoise(z, y);
        return (a + b + c) / 3f;
    }

    float Ridged01(float n01)
    {
        // 0.5 peak, 0/1 valleys
        float r = 1f - Mathf.Abs(n01 * 2f - 1f);
        return Mathf.Clamp01(r);
    }

    bool IsCave(int worldX, int worldY, int worldZ, int surfaceY)
    {
        if (!enableCaves) return false;
        if (worldY <= 5) return false;
        if (worldY >= surfaceY - caveMinDepthBelowSurface) return false;

        int s = settings.seed;

        // Depth: more caves deeper down (Minecraft-like)
        float depth01 = Mathf.InverseLerp(surfaceY, 0, worldY);
        float depthBoost = Mathf.Lerp(0.00f, 0.12f, depth01);

        // Spaghetti (ridged) tunnels
        float sx = (worldX + s * 73) * caveSpaghettiScale;
        float sy = (worldY + s * 97) * caveSpaghettiScale;
        float sz = (worldZ + s * 53) * caveSpaghettiScale;

        float nS = Noise3Cheap(sx, sy, sz);
        float ridged = Ridged01(nS);

        // Larger caverns (blobs)
        float cx = (worldX + s * 147) * caveCavernScale;
        float cy = (worldY + s * 191) * caveCavernScale;
        float cz = (worldZ + s * 171) * caveCavernScale;
        float nC = Noise3Cheap(cx, cy, cz);

        // Original style as a small contribution (keeps variety)
        float ax = (worldX + s * 13) * caveScale;
        float ay = (worldY + s * 29) * caveScale;
        float az = (worldZ + s * 41) * caveScale;
        float nO = Noise3Cheap(ax, ay, az);

        // Mix and threshold
        float v = Mathf.Lerp(ridged, nO, 0.25f);
        bool spaghetti = v > (caveSpaghettiThreshold - depthBoost);
        bool cavern = nC > (caveCavernThreshold - depthBoost * 0.5f);

        return spaghetti || cavern;
    }

    // =========================
    // Block sampling (terrain)
    // =========================

    public void GetOreSettings(out OreSettings o)
    {
        o = new OreSettings
        {
            coal = new OreLayer(countPerChunk: 20, veinSize: 17, yMin: 0, yMax: 127),
            iron = new OreLayer(countPerChunk: 20, veinSize: 9, yMin: 0, yMax: 63),
            gold = new OreLayer(countPerChunk: 9, veinSize: 9, yMin: 0, yMax: 31),
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
        public OreLayer coal, iron, gold, redstone, lapis, diamond, emerald;
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

        float riverMask = RiverMask01(worldX, worldZ, ocean);

        bool isOcean = minecraftLikeWater && ocean >= oceanThreshold;
        bool isRiver = minecraftLikeWater && riverMask > 0f;

        int waterLevel = SeaLevel;

        if (worldY > surfaceY)
        {
            if (minecraftLikeWater && worldY <= waterLevel)
            {
                if (isRiver)
                {
                    // Rivers: always fill to sea level
                    return BlockType.Water;
                }

                if (isOcean)
                {
                    int depth = waterLevel - surfaceY;
                    if (depth >= minWaterDepth) return BlockType.Water;
                }
            }
            return BlockType.Air;
        }

        bool underwater = minecraftLikeWater && surfaceY < waterLevel && (isOcean || isRiver);

        if (worldY == surfaceY)
        {
            if (underwater)
            {
                // Sandy shores / riverbeds
                return BlockType.Sand;
            }
            return BlockType.Grass;
        }

        int depthBelow = surfaceY - worldY;

        if (depthBelow <= Mathf.Max(1, topSoilDepth))
        {
            if (underwater && depthBelow <= 3) return BlockType.Sand;
            return BlockType.Dirt;
        }

        if (IsCave(worldX, worldY, worldZ, surfaceY))
            return BlockType.Air;

        return BlockType.Stone;
    }

    // =========================
    // World storage / edits
    // =========================

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
