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
        public Color forestGrass = new Color32(0x79, 0xC0, 0x5A, 0xFF);
        public Color forestFoliage = new Color32(0x59, 0xAE, 0x30, 0xFF);
        public Color spruceGrass = new Color32(0x81, 0x8D, 0x75, 0xFF);
        public Color spruceFoliage = new Color32(0x61, 0x99, 0x61, 0xFF);
        public Color jungleGrass = new Color32(0x59, 0xC9, 0x3C, 0xFF);
        public Color jungleFoliage = new Color32(0x30, 0xBB, 0x0B, 0xFF);
        public Color desertGrass = new Color32(0xBF, 0xB7, 0x55, 0xFF);
        public Color savannaGrass = new Color32(0xBF, 0xB7, 0x55, 0xFF);
    }

    [System.Serializable]
    public class BlockMaterials
    {
        public Material grassTop, grassSide, dirt, stone, sand, water, bedrock;
        public Material gravel, clay, cactusTop, cactusSide, cactusBottom, deadBush;
        public Material coalOre, ironOre, goldOre, redstoneOre, lapisOre, diamondOre, emeraldOre;
        public Material oakLogTop, oakLogSide, oakLeaves;
        public Material birchLogTop, birchLogSide, birchLeaves;
        public Material spruceLogTop, spruceLogSide, spruceLeaves;
        public Material jungleLogTop, jungleLogSide, jungleLeaves;
        public Material acaciaLogTop, acaciaLogSide, acaciaLeaves;
    }

    [System.Serializable]
    public class WorldSettings
    {
        public Transform player;
        public int viewDistanceInChunks = 8;
        [Header("Generation")]
        public bool useRandomSeed = false;
        public int seed = 12345;
        public BiomeOverride biomeOverride = BiomeOverride.None;
    }

    public enum BiomeOverride { None, Plains, Forest, BirchForest, Taiga, Jungle, Desert, Savanna, Ocean }
    public enum BiomeId { Plains, Forest, BirchForest, Taiga, Jungle, Desert, Savanna, Ocean }

    public BlockMaterials materials;
    public BiomeTints biomeTints;
    public WorldSettings settings;
    public PhysicsMaterial terrainColliderMaterial;

    public bool minecraftLikeWater = true;
    public bool gravityBlocksEnabled = true;
    public int gravityChecksPerFrame = 64;

    readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    readonly HashSet<Vector2Int> activeChunkCoords = new HashSet<Vector2Int>();
    readonly List<Vector2Int> neededChunks = new List<Vector2Int>();
    
    readonly Queue<Vector3Int> gravityQueue = new Queue<Vector3Int>();
    readonly HashSet<Vector3Int> gravityQueuedSet = new HashSet<Vector3Int>();

    int worldSeed;
    FastNoise noise;

    void Awake()
    {
        if (settings.useRandomSeed) settings.seed = Random.Range(-100000, 100000);
        worldSeed = settings.seed;
        if (biomeTints == null) biomeTints = new BiomeTints();
        noise = new FastNoise(worldSeed);
        Random.InitState(worldSeed);
    }

    void Start() { UpdateWorld(true); }
    void Update() { UpdateWorld(false); TickGravityBlocks(); }

    public Vector2Int WorldToChunkCoord(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / 16f), Mathf.FloorToInt(p.z / 16f));

    void UpdateWorld(bool force)
    {
        if (settings.player == null) return;

        Vector2Int pChunk = WorldToChunkCoord(settings.player.position);
        activeChunkCoords.Clear();
        neededChunks.Clear();

        int r = settings.viewDistanceInChunks;
        for (int dx = -r; dx <= r; dx++)
        for (int dz = -r; dz <= r; dz++)
        {
            Vector2Int c = new Vector2Int(pChunk.x + dx, pChunk.y + dz);
            activeChunkCoords.Add(c);
            if (!chunks.ContainsKey(c)) neededChunks.Add(c);
        }

        neededChunks.Sort((a, b) => Vector2Int.Distance(a, pChunk).CompareTo(Vector2Int.Distance(b, pChunk)));
        int budget = force ? neededChunks.Count : 4;
        for (int i = 0; i < Mathf.Min(budget, neededChunks.Count); i++) CreateChunk(neededChunks[i]);

        foreach (var kv in chunks) kv.Value.SetActive(activeChunkCoords.Contains(kv.Key));
    }

    void CreateChunk(Vector2Int c)
    {
        GameObject go = new GameObject($"Chunk_{c.x}_{c.y}");
        go.transform.parent = transform;
        go.transform.position = new Vector3(c.x * 16, 0, c.y * 16);
        Chunk ch = go.AddComponent<Chunk>();
        ch.Init(this, c, materials);
        chunks.Add(c, ch);
    }

    public BiomeId GetBiome(int x, int z)
    {
        if (settings.biomeOverride != BiomeOverride.None) return (BiomeId)((int)settings.biomeOverride - 1);

        float cont = Continentalness(x, z);
        if (cont < -0.35f) return BiomeId.Ocean;

        float temp = FBM2(x * 0.00125f, z * 0.00125f, 4, 0.52f, 2.0f, 1007);
        float humid = FBM2(x * 0.00125f, z * 0.00125f, 4, 0.50f, 2.05f, 2009);

        temp = Mathf.Clamp01(temp * 0.5f + 0.5f);
        humid = Mathf.Clamp01(humid * 0.5f + 0.5f);

        float hot = Mathf.SmoothStep(0.0f, 1.0f, temp);
        float wet = Mathf.SmoothStep(0.0f, 1.0f, humid);

        if (hot > 0.78f && wet < 0.35f) return BiomeId.Desert;
        if (hot > 0.72f && wet < 0.55f) return BiomeId.Savanna;
        if (hot > 0.68f && wet > 0.70f) return BiomeId.Jungle;
        if (hot < 0.28f) return BiomeId.Taiga;
        if (wet > 0.62f) return BiomeId.Forest;
        if (wet > 0.48f && hot > 0.42f) return BiomeId.BirchForest;
        return BiomeId.Plains;
    }

    public void GetBiomeTintsAt(int worldX, int worldZ, out Color grass, out Color foliage)
    {
        BiomeId b = GetBiome(worldX, worldZ);
        grass = biomeTints.plainsGrass;
        foliage = biomeTints.plainsFoliage;
        switch (b)
        {
            case BiomeId.Forest: grass = biomeTints.forestGrass; foliage = biomeTints.forestFoliage; break;
            case BiomeId.BirchForest: grass = biomeTints.forestGrass; foliage = biomeTints.forestFoliage; break;
            case BiomeId.Taiga: grass = biomeTints.spruceGrass; foliage = biomeTints.spruceFoliage; break;
            case BiomeId.Jungle: grass = biomeTints.jungleGrass; foliage = biomeTints.jungleFoliage; break;
            case BiomeId.Desert: grass = biomeTints.desertGrass; foliage = biomeTints.desertGrass; break;
            case BiomeId.Savanna: grass = biomeTints.savannaGrass; foliage = biomeTints.savannaGrass; break;
        }
    }

    float Continentalness(int x, int z)
    {
        float cont = FBM2(x * 0.0018f, z * 0.0018f, 4, 0.52f, 2.0f, 14011);
        float cont2 = FBM2(x * 0.0009f, z * 0.0009f, 3, 0.55f, 2.0f, 15011);
        float c = (cont * 0.75f + cont2 * 0.25f);
        return Mathf.Clamp(c, -1f, 1f);
    }

    public int GetLandHeight(int x, int z)
    {
        float cont = Continentalness(x, z);

        float baseN = FBM2(x * 0.0030f, z * 0.0030f, 5, 0.50f, 2.0f, 3001);
        float detail = FBM2(x * 0.0120f, z * 0.0120f, 3, 0.50f, 2.1f, 4001);

        float baseHeight = SeaLevel + 6;
        float inland = Mathf.Clamp01((cont + 0.35f) / 1.35f);
        float inlandPow = inland * inland;

        float hills = baseN * 18f + detail * 4f;
        float plateaus = FBM2(x * 0.0018f, z * 0.0018f, 4, 0.52f, 2.0f, 5001) * 10f;

        float h = baseHeight + inlandPow * (hills + plateaus) + cont * 28f;

        if (cont < -0.35f)
        {
            float t = Mathf.InverseLerp(-0.35f, -0.75f, cont);
            float oceanFloor = Mathf.Lerp(SeaLevel - 6, 18f, t * t);
            float floorVar = FBM2(x * 0.020f, z * 0.020f, 2, 0.55f, 2.0f, 6001) * 4f;
            h = oceanFloor + floorVar;
        }

        int ih = Mathf.Clamp(Mathf.RoundToInt(h), 4, 126);

        float river = Mathf.Abs(FBM2(x * 0.004f, z * 0.004f, 3, 0.5f, 2.0f, 7001));
        float riverMask = Mathf.SmoothStep(0.04f, 0.00f, river);
        if (cont > -0.25f)
        {
            float target = SeaLevel + 1;
            ih = Mathf.RoundToInt(Mathf.Lerp(ih, target, riverMask * 0.65f));
            ih = Mathf.Clamp(ih, 4, 126);
        }

        return ih;
    }

    public void GenerateColumn(int x, int z, BlockType[] col)
    {
        int h = GetLandHeight(x, z);
        BiomeId biome = GetBiome(x, z);

        bool beachZone = h >= SeaLevel - 2 && h <= SeaLevel + 2 && biome != BiomeId.Ocean;
        float beachN = FBM2(x * 0.02f, z * 0.02f, 2, 0.5f, 2.0f, 8001);
        bool sandBeach = beachZone && beachN > 0.10f;
        bool gravelBeach = beachZone && !sandBeach && beachN > -0.10f;

        int fillDepth = 4;
        int stoneStart = Mathf.Max(1, h - fillDepth);

        float bedNoise = noise.Noise2(x * 0.1f, z * 0.1f, 9500);

        for (int y = 0; y < VoxelData.ChunkHeight; y++)
        {
            BlockType t;

            if (y == 0) t = BedrockAt(x, y, z);
            else if (y > h) t = (y <= SeaLevel) ? BlockType.Water : BlockType.Air;
            else
            {
                if (IsCaveMask(x, y, z, h))
                {
                    t = BlockType.Air;
                }
                else
                {
                    if (y == h)
                    {
                        if (y <= SeaLevel)
                        {
                            if (biome == BiomeId.Ocean)
                            {
                                if (bedNoise > 0.3f) t = BlockType.Gravel;
                                else if (bedNoise < -0.3f) t = BlockType.Clay;
                                else if (bedNoise > 0.1f) t = BlockType.Dirt;
                                else t = BlockType.Sand;
                            }
                            else
                            {
                                if (bedNoise > 0.4f) t = BlockType.Gravel;
                                else t = BlockType.Dirt;
                            }
                        }
                        else
                        {
                            if (biome == BiomeId.Desert) t = BlockType.Sand;
                            else if (sandBeach) t = BlockType.Sand;
                            else if (gravelBeach) t = BlockType.Gravel;
                            else t = BlockType.Grass;
                        }
                    }
                    else if (y >= stoneStart)
                    {
                        if (biome == BiomeId.Desert) t = BlockType.Sand;
                        else if (y <= SeaLevel) 
                        {
                            if (bedNoise > 0.3f && biome == BiomeId.Ocean) t = BlockType.Gravel;
                            else t = BlockType.Dirt;
                        }
                        else if (sandBeach) t = BlockType.Sand;
                        else if (gravelBeach) t = BlockType.Gravel;
                        else t = BlockType.Dirt;
                    }
                    else
                    {
                        t = BlockType.Stone;
                    }
                }
            }

            col[y] = t;
        }
    }

    bool IsCaveMask(int x, int y, int z, int surfaceY)
    {
        return false;
    }

    BlockType BedrockAt(int x, int y, int z)
    {
        if (y == 0) return BlockType.Bedrock;
        int n = Hash3(x, y, z);
        int r = (n ^ (n >> 16)) & 255;
        if (y <= 4 && r < 110) return BlockType.Bedrock;
        return BlockType.Stone;
    }

    float FBM2(float x, float z, int octaves, float gain, float lacunarity, int salt)
    {
        float a = 1f;
        float f = 1f;
        float s = 0f;
        float nrm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            s += a * noise.Noise2(x * f, z * f, salt + i * 1013);
            nrm += a;
            a *= gain;
            f *= lacunarity;
        }
        return s / Mathf.Max(0.0001f, nrm);
    }

    public BlockType GetBlock(Vector3Int p)
    {
        if (p.y < 0 || p.y >= VoxelData.ChunkHeight) return BlockType.Air;
        Vector2Int c = WorldToChunkCoord(p);
        if (chunks.TryGetValue(c, out Chunk ch)) return ch.GetBlockLocal(p.x - c.x * 16, p.y, p.z - c.y * 16);
        return GetGeneratedBlock(p.x, p.y, p.z);
    }

    public BlockType GetGeneratedBlock(int x, int y, int z)
    {
        BlockType[] col = new BlockType[VoxelData.ChunkHeight];
        GenerateColumn(x, z, col);
        return col[y];
    }

    public void SetBlock(Vector3Int p, BlockType t)
    {
        if (p.y < 0 || p.y >= VoxelData.ChunkHeight) return;
        Vector2Int c = WorldToChunkCoord(p);
        if (chunks.TryGetValue(c, out Chunk ch))
        {
            Vector3Int loc = ch.WorldToLocal(p);
            ch.SetBlockLocal(loc, t);
            ch.Rebuild();
            
            if (loc.x == 0) Reb(c + Vector2Int.left);
            if (loc.x == 15) Reb(c + Vector2Int.right);
            if (loc.z == 0) Reb(c + Vector2Int.down);
            if (loc.z == 15) Reb(c + Vector2Int.up);

            if (gravityBlocksEnabled && (t == BlockType.Sand || t == BlockType.Gravel))
            {
                if (gravityQueuedSet.Add(p)) gravityQueue.Enqueue(p);
            }
            
            if (t == BlockType.Air)
            {
                Vector3Int above = p + Vector3Int.up;
                BlockType bAbove = GetBlock(above);
                if (bAbove == BlockType.Sand || bAbove == BlockType.Gravel)
                {
                    if (gravityQueuedSet.Add(above)) gravityQueue.Enqueue(above);
                }
            }
        }
    }

    void Reb(Vector2Int c) { if (chunks.TryGetValue(c, out Chunk ch)) ch.Rebuild(); }

    public int Hash3(int x, int y, int z)
    {
        unchecked
        {
            int h = worldSeed;
            h ^= x * 374761393;
            h = (h << 13) ^ h;
            h ^= y * 668265263;
            h = (h << 13) ^ h;
            h ^= z * 2147483647;
            h = (h << 13) ^ h;
            return h;
        }
    }

    public bool RandomChance(int x, int y, int z, int mod, int lt)
    {
        int v = Hash3(x, y, z);
        if (v < 0) v = -v;
        return (v % mod) < lt;
    }

    void TickGravityBlocks()
    {
        int lim = Mathf.Max(1, gravityChecksPerFrame);
        while (lim-- > 0 && gravityQueue.Count > 0)
        {
            Vector3Int p = gravityQueue.Dequeue();
            gravityQueuedSet.Remove(p);

            BlockType bt = GetBlock(p);
            if ((bt == BlockType.Sand || bt == BlockType.Gravel))
            {
                Vector3Int below = p + Vector3Int.down;
                BlockType bBelow = GetBlock(below);

                if (bBelow == BlockType.Air || bBelow == BlockType.Water)
                {
                    SetBlock(p, BlockType.Air);
                    SpawnFallingBlock(p, bt);
                }
            }
        }
    }

    void SpawnFallingBlock(Vector3Int p, BlockType type)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.position = p + new Vector3(0.5f, 0.5f, 0.5f);
        
        FallingBlock fb = go.AddComponent<FallingBlock>();
        fb.world = this;
        fb.type = type;

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null && materials != null)
        {
             if (type == BlockType.Sand) mr.sharedMaterial = materials.sand;
             else if (type == BlockType.Gravel) mr.sharedMaterial = materials.gravel;
        }
    }

    struct FastNoise
    {
        int seed;

        public FastNoise(int seed) { this.seed = seed; }

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        int Hash(int x, int y, int z, int salt)
        {
            unchecked
            {
                int h = seed ^ salt;
                h ^= x * 374761393;
                h = (h << 13) ^ h;
                h ^= y * 668265263;
                h = (h << 13) ^ h;
                h ^= z * 2147483647;
                h = (h << 13) ^ h;
                h *= 1274126177;
                return h;
            }
        }

        static float Grad2(int h, float x, float z)
        {
            int g = h & 7;
            float u = (g < 4) ? x : z;
            float v = (g < 4) ? z : x;
            float a = ((g & 1) == 0) ? u : -u;
            float b = ((g & 2) == 0) ? v : -v;
            return a + b;
        }

        static float Grad3(int h, float x, float y, float z)
        {
            int g = h & 15;
            float u = g < 8 ? x : y;
            float v = g < 4 ? y : (g == 12 || g == 14 ? x : z);
            float a = ((g & 1) == 0) ? u : -u;
            float b = ((g & 2) == 0) ? v : -v;
            return a + b;
        }

        public float Noise2(float x, float z, int salt)
        {
            int xi = Mathf.FloorToInt(x);
            int zi = Mathf.FloorToInt(z);
            float xf = x - xi;
            float zf = z - zi;

            float u = Fade(xf);
            float v = Fade(zf);

            int h00 = Hash(xi, 0, zi, salt);
            int h10 = Hash(xi + 1, 0, zi, salt);
            int h01 = Hash(xi, 0, zi + 1, salt);
            int h11 = Hash(xi + 1, 0, zi + 1, salt);

            float x1 = Lerp(Grad2(h00, xf, zf), Grad2(h10, xf - 1f, zf), u);
            float x2 = Lerp(Grad2(h01, xf, zf - 1f), Grad2(h11, xf - 1f, zf - 1f), u);
            float n = Lerp(x1, x2, v);
            return Mathf.Clamp(n * 0.7071f, -1f, 1f);
        }

        public float Noise3(float x, float y, float z, int salt)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            int zi = Mathf.FloorToInt(z);

            float xf = x - xi;
            float yf = y - yi;
            float zf = z - zi;

            float u = Fade(xf);
            float v = Fade(yf);
            float w = Fade(zf);

            int h000 = Hash(xi, yi, zi, salt);
            int h100 = Hash(xi + 1, yi, zi, salt);
            int h010 = Hash(xi, yi + 1, zi, salt);
            int h110 = Hash(xi + 1, yi + 1, zi, salt);
            int h001 = Hash(xi, yi, zi + 1, salt);
            int h101 = Hash(xi + 1, yi, zi + 1, salt);
            int h011 = Hash(xi, yi + 1, zi + 1, salt);
            int h111 = Hash(xi + 1, yi + 1, zi + 1, salt);

            float x00 = Lerp(Grad3(h000, xf, yf, zf), Grad3(h100, xf - 1f, yf, zf), u);
            float x10 = Lerp(Grad3(h010, xf, yf - 1f, zf), Grad3(h110, xf - 1f, yf - 1f, zf), u);
            float x01 = Lerp(Grad3(h001, xf, yf, zf - 1f), Grad3(h101, xf - 1f, yf, zf - 1f), u);
            float x11 = Lerp(Grad3(h011, xf, yf - 1f, zf - 1f), Grad3(h111, xf - 1f, yf - 1f, zf - 1f), u);

            float y0 = Lerp(x00, x10, v);
            float y1 = Lerp(x01, x11, v);

            float n = Lerp(y0, y1, w);
            return Mathf.Clamp(n * 0.57735f, -1f, 1f);
        }
    }
}