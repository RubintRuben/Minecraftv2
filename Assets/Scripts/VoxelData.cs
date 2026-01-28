using UnityEngine;

public static class VoxelData
{
    public const int ChunkSize = 16;
    public const int ChunkHeight = 128;

    public static readonly Vector3[] Verts = new Vector3[8]
    {
        new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(0,1,0),
        new Vector3(0,1,1), new Vector3(1,1,1), new Vector3(1,0,1), new Vector3(0,0,1)
    };

    public static readonly int[,] Tris = new int[6, 4]
    {
        {0,3,2,1}, {6,5,4,7}, {3,4,5,2}, {1,6,7,0}, {7,4,3,0}, {1,2,5,6}
    };

    public static readonly Vector3[] FaceChecks = new Vector3[6]
    {
        new Vector3(0,0,-1), new Vector3(0,0, 1), new Vector3(0,1, 0),
        new Vector3(0,-1,0), new Vector3(-1,0,0), new Vector3(1,0, 0),
    };

    public static readonly Vector2[] BaseUVs = new Vector2[4]
    {
        new Vector2(0,0), new Vector2(0,1), new Vector2(1,1), new Vector2(1,0)
    };
}

public enum BlockType : byte
{
    Air = 0,
    Grass = 1,
    Dirt = 2,
    Stone = 3,
    Sand = 4,
    Water = 5,
    Bedrock = 6,
    Gravel = 7,
    Clay = 8,
    
    CoalOre = 10,
    IronOre = 11,
    GoldOre = 12,
    RedstoneOre = 13,
    LapisOre = 14,
    DiamondOre = 15,
    EmeraldOre = 16,

    OakLog = 20,
    OakLeaves = 21,
    BirchLog = 22,
    BirchLeaves = 23,
    SpruceLog = 24,
    SpruceLeaves = 25,
    JungleLog = 26,
    JungleLeaves = 27,
    AcaciaLog = 28,
    AcaciaLeaves = 29,

    Cactus = 30,
    DeadBush = 31
}