using UnityEngine;

public static class VoxelData
{
    public const int ChunkSize = 16;
    public const int ChunkHeight = 64;

    public static readonly Vector3[] Verts = new Vector3[8]
    {
        new Vector3(0,0,0), 
        new Vector3(1,0,0), 
        new Vector3(1,1,0), 
        new Vector3(0,1,0), 
        new Vector3(0,1,1), 
        new Vector3(1,1,1), 
        new Vector3(1,0,1), 
        new Vector3(0,0,1)  
    };

    public static readonly int[,] Tris = new int[6, 4]
    {
        {0,3,2,1}, 
        {6,5,4,7}, 
        {3,4,5,2}, 
        {1,6,7,0}, 
        {7,4,3,0}, 
        {1,2,5,6}  
    };

    public static readonly Vector3[] FaceChecks = new Vector3[6]
    {
        new Vector3(0,0,-1), 
        new Vector3(0,0, 1), 
        new Vector3(0,1, 0), 
        new Vector3(0,-1,0), 
        new Vector3(-1,0,0), 
        new Vector3(1,0, 0), 
    };

    public static readonly Vector2[] BaseUVs = new Vector2[4]
    {
        new Vector2(0,0),
        new Vector2(0,1),
        new Vector2(1,1),
        new Vector2(1,0)
    };
}

public enum BlockType : byte
{
    Air = 0,
    Dirt = 1,
    Grass = 2
}
