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
        public Color desertGrass = new Color32(0xBF, 0xB7, 0x55, 0xFF);
        public Color desertFoliage = new Color32(0xAE, 0xA4, 0x2A, 0xFF);
        public Color mountainGrass = new Color32(0x8A, 0xB6, 0x8D, 0xFF);
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
        public int viewDistanceInChunks = 8;
        public int seed = 12345;
    }

    public enum BiomeType
    {
        Ocean,
        Beach,
        Plains,
        Forest,
        Desert,
        Mountains,
        Snow
    }

    public BlockMaterials materials;
    public BiomeTints biomeTints;
    public WorldSettings settings;
    public PhysicsMaterial terrainColliderMaterial;

    [Range(0f, 1f)] public float roughness = 0.25f;
    public bool enableCaves = true;

    private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    private readonly HashSet<Vector2Int> activeChunkCoords = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> neededChunks = new List<Vector2Int>(2048);
    
    // Sand physics
    private readonly Queue<Vector3Int> sandQueue = new Queue<Vector3Int>(4096);
    private readonly HashSet<Vector3Int> sandQueued = new HashSet<Vector3Int>();

    // Water physics
    private readonly Queue<Vector3Int> waterQueue = new Queue<Vector3Int>(4096);
    private readonly HashSet<Vector3Int> waterQueued = new HashSet<Vector3Int>();

    const int ChunkCreateBudgetPerFrame = 2;

    private void Awake()
    {
        Random.InitState(settings.seed);
        if (biomeTints == null) biomeTints = new BiomeTints();
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
        TickWater();
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

    public BiomeType GetBiome(int worldX, int worldZ)
    {
        float s = settings.seed;
        float temp = Mathf.PerlinNoise((worldX + s) * 0.001f, (worldZ + s) * 0.001f);
        float rain = Mathf.PerlinNoise((worldX + s + 500) * 0.001f, (worldZ + s + 500) * 0.001f);
        
        // Ocean check via noise (continental)
        float heightNoise = Mathf.PerlinNoise((worldX + s) * 0.002f, (worldZ + s) * 0.002f);
        if (heightNoise < 0.35f) return BiomeType.Ocean;

        // Mountain check
        if (heightNoise > 0.7f)
        {
            if (temp < 0.3f) return BiomeType.Snow;
            return BiomeType.Mountains;
        }

        if (temp > 0.6f && rain < 0.4f) return BiomeType.Desert;
        if (rain > 0.5f && temp > 0.3f) return BiomeType.Forest;
        
        return BiomeType.Plains;
    }

    public void GetBiomeTintsAt(int worldX, int worldZ, out Color grass, out Color foliage)
    {
        BiomeType b = GetBiome(worldX, worldZ);
        switch (b)
        {
            case BiomeType.Forest:
                grass = biomeTints.forestGrass;
                foliage = biomeTints.forestFoliage;
                break;
            case BiomeType.Desert:
                grass = biomeTints.desertGrass;
                foliage = biomeTints.desertFoliage;
                break;
            case BiomeType.Mountains:
                grass = biomeTints.mountainGrass;
                foliage = biomeTints.mountainGrass; 
                break;
            default:
                grass = biomeTints.plainsGrass;
                foliage = biomeTints.plainsFoliage;
                break;
        }
    }

    public int GetLandHeight(int worldX, int worldZ)
    {
        float s = settings.seed;
        
        // Base terrain noise
        float continental = Mathf.PerlinNoise((worldX + s) * 0.002f, (worldZ + s) * 0.002f);
        
        float baseH = SeaLevel;
        float multiplier = 10f;

        if (continental < 0.35f) // Ocean
        {
            baseH = 45f;
            multiplier = 10f;
        }
        else if (continental > 0.7f) // Mountains
        {
            baseH = 90f;
            multiplier = 40f;
        }
        else // Plains/Forest/Desert
        {
            baseH = 68f;
            multiplier = 15f;
        }

        // Add detail noise
        float detail = Mathf.PerlinNoise((worldX + s) * 0.02f, (worldZ + s) * 0.02f) * 2f - 1f;
        
        // River carving
        float riverNoise = Mathf.PerlinNoise((worldX + s + 9999) * 0.0008f, (worldZ + s + 9999) * 0.0008f);
        float riverVal = Mathf.Abs(riverNoise * 2f - 1f);
        
        float finalH = baseH + detail * multiplier * 0.5f;

        if (riverVal < 0.06f && continental > 0.35f) // River
        {
            float depth = Mathf.InverseLerp(0.06f, 0.0f, riverVal);
            finalH = Mathf.Lerp(finalH, SeaLevel - 4, depth);
        }

        return Mathf.RoundToInt(finalH);
    }

    bool IsCave(int worldX, int worldY, int worldZ)
    {
        if (!enableCaves) return false;
        if (worldY <= 4) return false;

        float s = settings.seed;

        // 3D Noise for caves (Spaghetti & Cheese)
        // Cheese noise (large hollows)
        float cheese = Noise3D((worldX + s) * 0.02f, (worldY + s) * 0.03f, (worldZ + s) * 0.02f);
        if (cheese > 0.65f) return true;

        // Tunnel noise (worm-like)
        float tunnel1 = Noise3D((worldX + s + 123) * 0.015f, (worldY + s + 456) * 0.015f, (worldZ + s + 789) * 0.015f);
        if (Mathf.Abs(tunnel1) < 0.12f) return true;

        return false;
    }

    // Simple 3D Perlin approximation (Unity only has 2D built-in effectively, so we stack 2D)
    float Noise3D(float x, float y, float z)
    {
        float xy = Mathf.PerlinNoise(x, y);
        float xz = Mathf.PerlinNoise(x, z);
        float yz = Mathf.PerlinNoise(y, z);
        float yx = Mathf.PerlinNoise(y, x);
        float zx = Mathf.PerlinNoise(z, x);
        float zy = Mathf.PerlinNoise(z, y);
        return (xy + xz + yz + yx + zx + zy) / 6f;
    }

    public BlockType GetGeneratedBlock(int worldX, int worldY, int worldZ)
    {
        if (worldY < 0 || worldY >= VoxelData.ChunkHeight) return BlockType.Air;
        if (worldY <= 4)
        {
            int h = Hash3(worldX, worldY, worldZ) % 5;
            if (worldY <= h) return BlockType.Bedrock;
        }

        int surfaceY = GetLandHeight(worldX, worldZ);
        BiomeType biome = GetBiome(worldX, worldZ);

        // Water checks
        if (worldY > surfaceY)
        {
            if (worldY <= SeaLevel) return BlockType.Water;
            return BlockType.Air;
        }

        // Cave checks
        if (IsCave(worldX, worldY, worldZ))
        {
            if (worldY <= SeaLevel) return BlockType.Water; // Flooded caves low down? Or just air. Let's do air above deep.
            // Actually MC caves below sea level are usually not flooded unless connected to ocean, but for simplicity let's keep them air unless it's ocean
            if (biome == BiomeType.Ocean && worldY > surfaceY - 5) return BlockType.Water;
            return BlockType.Air;
        }

        // Surface blocks
        if (worldY == surfaceY)
        {
            if (surfaceY <= SeaLevel + 2 && (biome == BiomeType.Desert || biome == BiomeType.Beach || biome == BiomeType.Ocean)) return BlockType.Sand;
            if (biome == BiomeType.Desert) return BlockType.Sand;
            if (biome == BiomeType.Mountains && surfaceY > 95) return BlockType.Stone; // Snow cap logic could go here
            return BlockType.Grass;
        }

        // Subsurface
        if (surfaceY > worldY && surfaceY - worldY < 4)
        {
            if (biome == BiomeType.Desert) return BlockType.Sand;
            if (surfaceY <= SeaLevel + 2 && (biome == BiomeType.Beach || biome == BiomeType.Ocean)) return BlockType.Sand;
             return BlockType.Dirt;
        }

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
        
        if (type == BlockType.Sand) EnqueueSand(worldPos);
        if (type == BlockType.Water) EnqueueWater(worldPos);
        
        if (prev != BlockType.Air && type == BlockType.Air)
        {
            Vector3Int above = new Vector3Int(worldPos.x, worldPos.y + 1, worldPos.z);
            if (GetBlock(above) == BlockType.Sand) EnqueueSand(above);
            if (GetBlock(above) == BlockType.Water) EnqueueWater(above);
            
            // Trigger neighbor water updates
            EnqueueWater(new Vector3Int(worldPos.x + 1, worldPos.y, worldPos.z));
            EnqueueWater(new Vector3Int(worldPos.x - 1, worldPos.y, worldPos.z));
            EnqueueWater(new Vector3Int(worldPos.x, worldPos.y, worldPos.z + 1));
            EnqueueWater(new Vector3Int(worldPos.x, worldPos.y, worldPos.z - 1));
        }
    }

    private void RebuildChunkIfExists(Vector2Int coord)
    {
        if (chunks.TryGetValue(coord, out Chunk ch)) ch.Rebuild();
    }

    private void EnqueueSand(Vector3Int pos)
    {
        if (!sandQueued.Add(pos)) return;
        sandQueue.Enqueue(pos);
    }
    
    private void EnqueueWater(Vector3Int pos)
    {
        if (!waterQueued.Add(pos)) return;
        waterQueue.Enqueue(pos);
    }

    private void TickSand()
    {
        int n = 32;
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

    private void TickWater()
    {
        int n = 32; // Limit water updates per frame to avoid lag
        for (int i = 0; i < n; i++)
        {
            if (waterQueue.Count == 0) break;
            Vector3Int p = waterQueue.Dequeue();
            waterQueued.Remove(p);

            BlockType self = GetBlock(p);
            if (self != BlockType.Water) continue;

            // Try flow down
            Vector3Int below = new Vector3Int(p.x, p.y - 1, p.z);
            if (below.y >= 0)
            {
                BlockType bBelow = GetBlock(below);
                if (bBelow == BlockType.Air)
                {
                    SetBlock(below, BlockType.Water); // Flow down
                    continue; 
                }
                else if (bBelow != BlockType.Water)
                {
                    // Hit ground, try spread
                    TrySpreadWater(p, new Vector3Int(p.x + 1, p.y, p.z));
                    TrySpreadWater(p, new Vector3Int(p.x - 1, p.y, p.z));
                    TrySpreadWater(p, new Vector3Int(p.x, p.y, p.z + 1));
                    TrySpreadWater(p, new Vector3Int(p.x, p.y, p.z - 1));
                }
            }
        }
    }

    private void TrySpreadWater(Vector3Int source, Vector3Int target)
    {
        if (GetBlock(target) == BlockType.Air)
        {
             SetBlock(target, BlockType.Water);
        }
    }

    private void SpawnFallingSandEntity(Vector3Int p)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.position = new Vector3(p.x + 0.5f, p.y + 0.5f, p.z + 0.5f);
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null && materials != null && materials.sand != null) mr.sharedMaterial = materials.sand;
        var rb = go.AddComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
        var fs = go.AddComponent<FallingSandEntity>();
        fs.world = this;
    }

    public int Hash3(int x, int y, int z)
    {
        unchecked
        {
            int h = settings.seed;
            h = (h * 397) ^ x;
            h = (h * 397) ^ z;
            h = (h * 397) ^ y;
            h ^= (h << 13);
            h ^= (h >> 17);
            h ^= (h << 5);
            return h;
        }
    }
}