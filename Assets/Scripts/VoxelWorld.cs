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
        public Material goldOre;
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

    static float N01(float v) => Mathf.Clamp01(v);
    static float N11(float v01) => v01 * 2f - 1f;

    float Perlin01(float x, float z, int ox, int oz, float scale)
    {
        int s = settings.seed;
        return Mathf.PerlinNoise((x + s * 131 + ox) * scale, (z + s * 197 + oz) * scale);
    }

    float FBM01(float x, float z, float scale, int octaves, float lacunarity, float persistence, int ox, int oz)
    {
        float sum = 0f;
        float amp = 1f;
        float freq = 1f;
        float norm = 0f;

        int o = Mathf.Max(1, octaves);
        for (int i = 0; i < o; i++)
        {
            float n = Perlin01(x * freq, z * freq, ox + i * 17, oz - i * 23, scale);
            sum += n * amp;
            norm += amp;
            amp *= persistence;
            freq *= lacunarity;
        }

        if (norm <= 0.00001f) return 0.5f;
        return sum / norm;
    }

    float Ridged01(float x, float z, float scale, int octaves, float lacunarity, float persistence, int ox, int oz)
    {
        float sum = 0f;
        float amp = 1f;
        float freq = 1f;
        float norm = 0f;

        int o = Mathf.Max(1, octaves);
        for (int i = 0; i < o; i++)
        {
            float n = Perlin01(x * freq, z * freq, ox + i * 29, oz + i * 31, scale);
            n = 1f - Mathf.Abs(n * 2f - 1f);
            n *= n;
            sum += n * amp;
            norm += amp;
            amp *= persistence;
            freq *= lacunarity;
        }

        if (norm <= 0.00001f) return 0f;
        return sum / norm;
    }

    public void GetBiomeParams(int worldX, int worldZ, out float temp, out float rain, out float forest, out float ocean)
    {
        int s = settings.seed;

        temp = Mathf.PerlinNoise((worldX + s * 11) * BiomeTempScale, (worldZ + s * 11) * BiomeTempScale);
        rain = Mathf.PerlinNoise((worldX + s * 23) * BiomeRainScale, (worldZ + s * 23) * BiomeRainScale);

        forest = Mathf.PerlinNoise((worldX + s * 37) * BiomeForestScale, (worldZ + s * 37) * BiomeForestScale);

        ocean = Mathf.PerlinNoise((worldX + s * 101) * oceanScale, (worldZ + s * 101) * oceanScale);
    }

    float OceanMask01(int worldX, int worldZ)
    {
        GetBiomeParams(worldX, worldZ, out _, out _, out _, out float ocean01);
        return N01(Mathf.InverseLerp(oceanThreshold, 0.0f, ocean01));
    }

    public int GetLandHeight(int worldX, int worldZ)
    {
        float r = Mathf.Lerp(0.85f, 1.25f, roughness);

        float baseN = FBM01(worldX, worldZ, 0.00105f * r, 4, 2.0f, 0.5f, 17001, -9001);
        float hillN = FBM01(worldX, worldZ, 0.00225f * r, 4, 2.0f, 0.52f, -31001, 12001);
        float selN = FBM01(worldX, worldZ, 0.00072f * r, 2, 2.0f, 0.5f, 9001, 7001);

        float blended = Mathf.Lerp(baseN, hillN, Mathf.SmoothStep(0.0f, 1.0f, selN));
        float hills = N11(blended);

        float ridge = Ridged01(worldX, worldZ, 0.00115f * r, 3, 2.0f, 0.55f, 33001, -17001);
        float mountains = Mathf.Pow(ridge, 2.25f);

        float oceanMask = OceanMask01(worldX, worldZ);

        float h = SeaLevel + 10f + hills * 18f + mountains * 42f;

        if (oceanMask > 0.001f)
        {
            float oceanFloor = SeaLevel - 10f + N11(FBM01(worldX, worldZ, 0.0028f * r, 2, 2.0f, 0.5f, 41001, 21001)) * 3.5f;
            h = Mathf.Lerp(h, oceanFloor, Mathf.Clamp01(oceanMask));
        }

        int height = Mathf.RoundToInt(h);
        height = Mathf.Clamp(height, 2, VoxelData.ChunkHeight - 2);

        if (minecraftLikeWater && oceanMask > 0.15f && height < SeaLevel - 1)
            height = SeaLevel - 1;

        return height;
    }

    float CaveNoise01(int worldX, int worldY, int worldZ)
    {
        int s = settings.seed;

        float sx = (worldX + s * 13) * caveScale;
        float sy = (worldY + s * 29) * caveScale;
        float sz = (worldZ + s * 41) * caveScale;

        float a = Mathf.PerlinNoise(sx, sy);
        float b = Mathf.PerlinNoise(sy, sz);
        float c = Mathf.PerlinNoise(sx, sz);

        float v = (a * 0.34f + b * 0.33f + c * 0.33f);

        float warp = Mathf.PerlinNoise((worldX + s * 97) * caveScale * 0.55f, (worldZ + s * 101) * caveScale * 0.55f);
        v = Mathf.Lerp(v, warp, 0.18f);

        return v;
    }

    bool IsCave(int worldX, int worldY, int worldZ, int surfaceY)
    {
        if (!enableCaves) return false;
        if (worldY <= 4) return false;

        int minTop = Mathf.Max(0, surfaceY - Mathf.Max(1, caveMinDepthBelowSurface));
        if (worldY >= minTop) return false;

        float y01 = Mathf.InverseLerp(0f, SeaLevel + 5f, worldY);
        float t = Mathf.Lerp(caveThreshold - 0.10f, caveThreshold + 0.05f, y01);

        float v = CaveNoise01(worldX, worldY, worldZ);

        float rav = Mathf.PerlinNoise((worldX + settings.seed * 211) * caveScale * 0.30f, (worldZ + settings.seed * 223) * caveScale * 0.30f);
        float band = Mathf.PerlinNoise((worldY + settings.seed * 227) * caveScale * 0.70f, (worldX + settings.seed * 229) * caveScale * 0.10f);
        float carve = rav * 0.70f + band * 0.30f;

        float extra = Mathf.Lerp(0.0f, 0.09f, carve);

        return v > (t - extra);
    }

    public void GetOreSettings(out OreSettings o)
    {
        o = new OreSettings
        {
            coal = new OreLayer(countPerChunk: 20, veinSize: 17, yMin: 0, yMax: 127),
            iron = new OreLayer(countPerChunk: 20, veinSize: 9, yMin: 0, yMax: 63),
            gold = new OreLayer(countPerChunk: 4, veinSize: 13, yMin: 0, yMax: 32),
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

    bool IsBeach(int surfaceY, float oceanMask)
    {
        if (surfaceY <= SeaLevel + 1) return true;
        if (oceanMask > 0.12f && surfaceY <= SeaLevel + 3) return true;
        return false;
    }

    bool IsLakeHere(int worldX, int worldZ, int surfaceY, float oceanMask)
    {
        if (oceanMask > 0.10f) return false;
        if (surfaceY >= SeaLevel - 1) return false;
        int h = Hash3(worldX, surfaceY, worldZ);
        int v = Mathf.Abs(h) % 1000;
        return v < 6;
    }

    public BlockType GetGeneratedBlock(int worldX, int worldY, int worldZ)
    {
        if (worldY < 0 || worldY >= VoxelData.ChunkHeight) return BlockType.Air;

        if (worldY <= 4)
        {
            int maxY = 1 + (Mathf.Abs(Hash3(worldX, 0, worldZ)) % 5);
            if (worldY < maxY) return BlockType.Bedrock;
        }

        float oceanMask = OceanMask01(worldX, worldZ);
        int surfaceY = GetLandHeight(worldX, worldZ);

        bool ocean = minecraftLikeWater && oceanMask > 0.15f;
        bool lake = minecraftLikeWater && IsLakeHere(worldX, worldZ, surfaceY, oceanMask);

        if (worldY > surfaceY)
        {
            if (minecraftLikeWater && worldY <= SeaLevel)
            {
                int depth = SeaLevel - surfaceY;
                if ((ocean && depth >= minWaterDepth) || lake)
                    return BlockType.Water;
            }
            return BlockType.Air;
        }

        int depthBelow = surfaceY - worldY;

        if (worldY == surfaceY)
        {
            if (minecraftLikeWater && surfaceY < SeaLevel) return BlockType.Sand;
            if (IsBeach(surfaceY, oceanMask)) return BlockType.Sand;
            return BlockType.Grass;
        }

        int soil = Mathf.Clamp(topSoilDepth, 1, 6);

        if (depthBelow <= soil)
        {
            if (minecraftLikeWater && surfaceY <= SeaLevel + 2 && depthBelow <= 3) return BlockType.Sand;
            if (oceanMask > 0.12f && surfaceY <= SeaLevel + 4 && depthBelow <= 4) return BlockType.Sand;
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
